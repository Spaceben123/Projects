using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds and maintains a 2D ground-track map inside a given parent RectTransform: Earth's own
/// day texture as a background, plus a live marker for every tracked SatelliteFlareController
/// craft, positioned from its real orbital latitude/longitude. Clicking a marker flies the
/// camera to that craft, same as clicking its glare in the 3D view.
/// </summary>
public class GroundTrackMapPanel : MonoBehaviour
{
    const float MarkerSizePixels = 10f;
    const float MapRefreshIntervalSeconds = 0.25f;

    RectTransform _mapRoot;
    RectTransform _markersRoot;
    Transform _earthTransform;
    CameraController _cameraController;
    float _refreshAccumulatorSeconds;

    readonly Dictionary<SatelliteFlareController, RectTransform> _markersByCraft =
        new Dictionary<SatelliteFlareController, RectTransform>();
    readonly List<SatelliteFlareController> _staleMarkersBuffer = new List<SatelliteFlareController>();

    /// <summary>The map's own RectTransform, sized to <paramref name="mapSize"/> passed to Initialize; callers position it within their layout.</summary>
    public RectTransform RootRect => _mapRoot;

    /// <summary>Builds the map UI as a child of the given parent and starts tracking craft positions.</summary>
    public void Initialize(Transform parent, Vector2 mapSize, Texture dayTexture, Transform earthTransform, CameraController cameraController)
    {
        _earthTransform = earthTransform;
        _cameraController = cameraController;

        var mapGo = new GameObject("GroundTrackMap", typeof(RectTransform));
        mapGo.transform.SetParent(parent, false);
        _mapRoot = mapGo.GetComponent<RectTransform>();
        _mapRoot.sizeDelta = mapSize;

        var mapImage = mapGo.AddComponent<RawImage>();
        mapImage.texture = dayTexture;
        mapImage.color = Color.white;

        var markersGo = new GameObject("Markers", typeof(RectTransform));
        markersGo.transform.SetParent(mapGo.transform, false);
        _markersRoot = markersGo.GetComponent<RectTransform>();
        _markersRoot.anchorMin = Vector2.zero;
        _markersRoot.anchorMax = Vector2.one;
        _markersRoot.offsetMin = Vector2.zero;
        _markersRoot.offsetMax = Vector2.zero;
    }

    void Update()
    {
        if (_mapRoot == null || _earthTransform == null)
            return;

        _refreshAccumulatorSeconds += Time.unscaledDeltaTime;
        if (_refreshAccumulatorSeconds < MapRefreshIntervalSeconds)
            return;
        _refreshAccumulatorSeconds = 0f;

        SyncMarkers();
        RepositionMarkers();
    }

    void SyncMarkers()
    {
        IReadOnlyList<SatelliteFlareController> craftList = SatelliteFlareController.All;

        for (int i = 0; i < craftList.Count; i++)
        {
            SatelliteFlareController craft = craftList[i];
            if (craft != null && !_markersByCraft.ContainsKey(craft))
                _markersByCraft[craft] = CreateMarker(craft);
        }

        _staleMarkersBuffer.Clear();
        foreach (KeyValuePair<SatelliteFlareController, RectTransform> pair in _markersByCraft)
        {
            if (pair.Key == null || !IsStillTracked(craftList, pair.Key))
                _staleMarkersBuffer.Add(pair.Key);
        }

        for (int i = 0; i < _staleMarkersBuffer.Count; i++)
        {
            if (_markersByCraft.TryGetValue(_staleMarkersBuffer[i], out RectTransform marker) && marker != null)
                Destroy(marker.gameObject);
            _markersByCraft.Remove(_staleMarkersBuffer[i]);
        }
    }

    static bool IsStillTracked(IReadOnlyList<SatelliteFlareController> craftList, SatelliteFlareController craft)
    {
        for (int i = 0; i < craftList.Count; i++)
            if (craftList[i] == craft)
                return true;
        return false;
    }

    RectTransform CreateMarker(SatelliteFlareController craft)
    {
        var markerGo = new GameObject($"Marker_{craft.name}", typeof(RectTransform));
        markerGo.transform.SetParent(_markersRoot, false);
        var rect = markerGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(MarkerSizePixels, MarkerSizePixels);

        var image = markerGo.AddComponent<Image>();
        image.color = SpaceObjectCategoryStyle.GetColor(craft.Category);

        var button = markerGo.AddComponent<Button>();
        button.targetGraphic = image;

        Transform craftTransform = craft.transform;
        CameraController cameraController = _cameraController;
        button.onClick.AddListener(() =>
        {
            if (cameraController != null)
                cameraController.FocusOnTarget(craftTransform);
        });

        return rect;
    }

    void RepositionMarkers()
    {
        float earthRadiusUnits = GeoUtils.EarthRadiusUnits;
        Vector2 mapSize = _mapRoot.sizeDelta;

        foreach (KeyValuePair<SatelliteFlareController, RectTransform> pair in _markersByCraft)
        {
            if (pair.Key == null || pair.Value == null)
                continue;

            Vector3 offsetFromEarth = pair.Key.transform.position - _earthTransform.position;
            (float latDeg, float lonDeg) = GeoUtils.WorldToLatLon(offsetFromEarth, earthRadiusUnits);
            Vector2 uv = GeoUtils.LatLonToUV(latDeg, lonDeg);

            pair.Value.anchoredPosition = new Vector2(
                (uv.x - 0.5f) * mapSize.x,
                (uv.y - 0.5f) * mapSize.y);
        }
    }
}
