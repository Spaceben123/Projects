using UnityEngine;
using UnityEngine.InputSystem;

// Attach to Main Camera.
// Default: cursor locked, mouse look active.
// Middle mouse: toggle free cursor (for UI/globe interaction).
// Double left click: fly to and frame whatever was clicked (planet, moon, station, ship).
// Glare-only objects (SatelliteFlareController dots) are hit-tested by screen-space proximity
// to the click rather than a 3D raycast, since their real collider is far smaller on screen
// than the glare you actually see; everything else still uses a physics raycast.
// WASD: forward/back and strafe in the camera's local plane; Q/E drop/rise.
public class CameraController : MonoBehaviour
{
    [SerializeField] float _lookSensitivity = 0.15f;
    [SerializeField] float _moveSpeed = 8f;
    [SerializeField] float _pitchMin = -89f;
    [SerializeField] float _pitchMax = 89f;
    [SerializeField] float _focusDuration = 0.75f;
    [SerializeField] float _focusDistanceMultiplier = 2.25f;
    [SerializeField] float _focusPadding = 1.5f;
    [SerializeField] float _minimumFocusDistance = 3f;
    [SerializeField] float _maximumFocusDistance = 100f;
    [SerializeField] LayerMask _focusLayers = ~0;
    // Maximum gap between the two presses of a focus double click. Serialized
    // because it is a feel value, not a correctness one.
    [SerializeField] float _doubleClickSeconds = 0.3f;
    // Glare-only objects (ships, stations, ...) are represented by a SatelliteFlareController
    // dot whose on-screen size has nothing to do with their near-invisible physical collider,
    // so they're hit-tested by screen-space distance from the click instead of a 3D raycast.
    // This is generous enough to forgive imprecise clicking on a small, distant glint.
    [SerializeField] float _flareIconClickRadiusPixels = 28f;

    const float MinimumDirectionSqrMagnitude = 0.0001f;
    // How far the cursor may drift between the two presses and still count as a
    // double click. Only meaningful while the cursor is free — under
    // CursorLockMode.Locked the reported position never moves off screen centre.
    const float DoubleClickMaxDriftPixels = 16f;

    bool _cameraMode = true;
    bool _isFocusing;
    float _pitch;
    float _yaw;
    float _focusElapsed;
    float _lastLeftClickTime = float.NegativeInfinity;
    Vector2 _lastLeftClickPosition;
    Vector3 _focusStartPosition;
    Vector3 _focusDestination;
    Quaternion _focusStartRotation;
    Quaternion _focusDestinationRotation;

    void Start()
    {
        _yaw = transform.eulerAngles.y;
        _pitch = transform.eulerAngles.x;
        if (_pitch > 180f) _pitch -= 360f;
        SetCameraMode(true);
    }

    void Update()
    {
        if (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame)
            SetCameraMode(!_cameraMode);

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            HandleLeftClick();

        if (_isFocusing)
        {
            UpdateFocusTransition();
            return;
        }

        if (_cameraMode && Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            _yaw += delta.x * _lookSensitivity;
            _pitch = Mathf.Clamp(_pitch - delta.y * _lookSensitivity, _pitchMin, _pitchMax);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        if (Keyboard.current != null)
        {
            Vector3 move = Vector3.zero;
            if (Keyboard.current.wKey.isPressed) move += transform.forward;
            if (Keyboard.current.sKey.isPressed) move -= transform.forward;
            if (Keyboard.current.aKey.isPressed) move -= transform.right;
            if (Keyboard.current.dKey.isPressed) move += transform.right;
            // Vertical pan kept on Q/E: W/S used to own it, and losing the axis
            // entirely would make it impossible to rise off a planet's surface
            // without pitching the camera first.
            if (Keyboard.current.eKey.isPressed) move += transform.up;
            if (Keyboard.current.qKey.isPressed) move -= transform.up;
            if (move != Vector3.zero)
                transform.position += move * (_moveSpeed * Time.deltaTime);
        }
    }

    // Focus is a DOUBLE click, not a single one: a single left click belongs to
    // NationSelectionSystem's click-to-select, so firing both off the same press
    // meant every nation selection also flew the camera away from the globe.
    void HandleLeftClick()
    {
        Vector2 position = Mouse.current.position.ReadValue();

        // Unscaled time so the window is unaffected by pausing or time-scale changes.
        bool withinTime = Time.unscaledTime - _lastLeftClickTime <= _doubleClickSeconds;
        bool withinDrift = (position - _lastLeftClickPosition).sqrMagnitude
                           <= DoubleClickMaxDriftPixels * DoubleClickMaxDriftPixels;

        if (withinTime && withinDrift)
        {
            // Consume the pair so a third click starts a fresh one instead of
            // chaining into a second focus.
            _lastLeftClickTime = float.NegativeInfinity;
            TryFocusClickedObject();
            return;
        }

        _lastLeftClickTime = Time.unscaledTime;
        _lastLeftClickPosition = position;
    }

    void TryFocusClickedObject()
    {
        Camera camera = GetComponent<Camera>();
        if (camera == null || Mouse.current == null) return;

        Vector2 mousePosition = Mouse.current.position.ReadValue();

        Transform flareTarget = FindNearestFlareIcon(camera, mousePosition);
        if (flareTarget != null)
        {
            FocusOnPoint(flareTarget.position, 0f);
            return;
        }

        Ray ray = camera.ScreenPointToRay(new Vector3(mousePosition.x, mousePosition.y, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, _focusLayers, QueryTriggerInteraction.Ignore))
            return;

        Bounds targetBounds = hit.collider.bounds;
        float targetRadius = Mathf.Max(targetBounds.extents.x, targetBounds.extents.y, targetBounds.extents.z);
        FocusOnPoint(targetBounds.center, targetRadius);
    }

    // Finds the closest registered glare dot (SatelliteFlareController) to the click, in screen
    // pixels, within _flareIconClickRadiusPixels. Objects behind the camera are ignored.
    Transform FindNearestFlareIcon(Camera camera, Vector2 mousePosition)
    {
        Transform nearest = null;
        float nearestSqrDistancePixels = _flareIconClickRadiusPixels * _flareIconClickRadiusPixels;

        var flares = SatelliteFlareController.All;
        for (int i = 0; i < flares.Count; i++)
        {
            Transform candidate = flares[i].transform;
            Vector3 screenPoint = camera.WorldToScreenPoint(candidate.position);
            if (screenPoint.z <= 0f)
                continue;

            float sqrDistancePixels = ((Vector2)screenPoint - mousePosition).sqrMagnitude;
            if (sqrDistancePixels <= nearestSqrDistancePixels)
            {
                nearestSqrDistancePixels = sqrDistancePixels;
                nearest = candidate;
            }
        }

        return nearest;
    }

    // Shared by both hit-testing paths: frames a target of the given radius (0 for a point-like
    // glare object) centred in view at a distance derived from that radius.
    void FocusOnPoint(Vector3 targetCenter, float targetRadius)
    {
        Vector3 directionFromTarget = transform.position - targetCenter;

        if (directionFromTarget.sqrMagnitude < MinimumDirectionSqrMagnitude)
            directionFromTarget = -transform.forward;
        else
            directionFromTarget.Normalize();

        float focusDistance = Mathf.Clamp(
            targetRadius * _focusDistanceMultiplier + _focusPadding,
            _minimumFocusDistance,
            _maximumFocusDistance);

        BeginFocusTransition(targetCenter + directionFromTarget * focusDistance, targetCenter);
    }

    /// <summary>
    /// Flies and focuses the camera onto the given target, exactly like double-clicking it in
    /// the 3D view. Intended for UI callers (e.g. clicking a craft in the Information panel's
    /// list or ground-track map) that want the same "fly to and frame" behavior.
    /// </summary>
    public void FocusOnTarget(Transform target, float targetRadius = 0f)
    {
        if (target == null)
            return;

        FocusOnPoint(target.position, targetRadius);
    }

    void BeginFocusTransition(Vector3 destination, Vector3 targetCenter)
    {
        _isFocusing = true;
        _focusElapsed = 0f;
        _focusStartPosition = transform.position;
        _focusDestination = destination;
        _focusStartRotation = transform.rotation;
        _focusDestinationRotation = Quaternion.LookRotation(targetCenter - destination, Vector3.up);
    }

    void UpdateFocusTransition()
    {
        _focusElapsed += Time.deltaTime;
        float normalizedTime = _focusDuration <= 0f ? 1f : Mathf.Clamp01(_focusElapsed / _focusDuration);
        float easedTime = normalizedTime * normalizedTime * (3f - 2f * normalizedTime);

        transform.position = Vector3.Lerp(_focusStartPosition, _focusDestination, easedTime);
        transform.rotation = Quaternion.Slerp(_focusStartRotation, _focusDestinationRotation, easedTime);

        if (normalizedTime < 1f) return;

        _isFocusing = false;
        _yaw = transform.eulerAngles.y;
        _pitch = transform.eulerAngles.x;
        if (_pitch > 180f) _pitch -= 360f;
    }

    void SetCameraMode(bool on)
    {
        _cameraMode = on;
        Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !on;
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && _cameraMode)
            Cursor.lockState = CursorLockMode.Locked;
    }
}
