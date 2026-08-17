using UnityEngine;

/// <summary>
/// Thin wrapper around a child TrailRenderer, enabled only while the owning OrbitingBody is
/// Maneuvering or while a scripted launch/reentry sequence forces it on - so idle/coasting
/// objects never pay trail-rendering cost. Pattern: SurfaceSiteMarkerRenderer - a component
/// that polls an owner each frame and toggles a child visual.
/// </summary>
public class EngineExhaustTrail : MonoBehaviour
{
    [SerializeField] OrbitingBody _orbitingBody;
    [SerializeField] TrailRenderer _trailRenderer;

    bool _forcedActive;

    void LateUpdate()
    {
        if (_trailRenderer == null)
            return;

        bool shouldBeActive = _forcedActive ||
            (_orbitingBody != null && _orbitingBody.CurrentState == OrbitingBody.OrbitalState.Maneuvering);

        if (_trailRenderer.enabled != shouldBeActive)
            _trailRenderer.enabled = shouldBeActive;
    }

    /// <summary>Forces the trail on/off regardless of orbital state, for scripted launch/reentry sequences.</summary>
    public void SetForcedActive(bool forcedActive)
    {
        _forcedActive = forcedActive;
    }
}
