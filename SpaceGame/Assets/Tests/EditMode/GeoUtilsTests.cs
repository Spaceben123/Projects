using NUnit.Framework;
using UnityEngine;

public class GeoUtilsTests
{
    [Test]
    public void LatLonToWorld_PrimeMeridianEquator_ReturnsNegativeX()
    {
        Vector3 r = GeoUtils.LatLonToWorld(0f, 0f, 10f);
        Assert.That(r.x, Is.EqualTo(-10f).Within(0.001f));
        Assert.That(r.y, Is.EqualTo(0f).Within(0.001f));
        Assert.That(r.z, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void LatLonToWorld_NorthPole_ReturnsPositiveY()
    {
        Vector3 r = GeoUtils.LatLonToWorld(90f, 0f, 10f);
        Assert.That(r.x, Is.EqualTo(0f).Within(0.001f));
        Assert.That(r.y, Is.EqualTo(10f).Within(0.001f));
        Assert.That(r.z, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void LatLonToWorld_ResultHasCorrectMagnitude()
    {
        Vector3 r = GeoUtils.LatLonToWorld(51.5f, -0.1f, 10f);
        Assert.That(r.magnitude, Is.EqualTo(10f).Within(0.001f));
    }

    [Test]
    public void WorldToLatLon_RoundTrip_London()
    {
        float lat = 51.5f, lon = -0.1f;
        Vector3 world = GeoUtils.LatLonToWorld(lat, lon, 10f);
        var (rLat, rLon) = GeoUtils.WorldToLatLon(world, 10f);
        Assert.That(rLat, Is.EqualTo(lat).Within(0.01f));
        Assert.That(rLon, Is.EqualTo(lon).Within(0.01f));
    }

    [Test]
    public void LatLonToUV_PrimeMeridianEquator_ReturnsCentre()
    {
        Vector2 uv = GeoUtils.LatLonToUV(0f, 0f);
        Assert.That(uv.x, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(uv.y, Is.EqualTo(0.5f).Within(0.001f));
    }

    [Test]
    public void LatLonToUV_NorthPole_ReturnsTopEdge()
    {
        Vector2 uv = GeoUtils.LatLonToUV(90f, 0f);
        Assert.That(uv.y, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void GreatCircleAngle_SamePoint_ReturnsZero()
    {
        float a = GeoUtils.GreatCircleAngle(35f, 139f, 35f, 139f);
        Assert.That(a, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void GreatCircleAngle_Antipodes_ReturnsPi()
    {
        float a = GeoUtils.GreatCircleAngle(0f, 0f, 0f, 180f);
        Assert.That(a, Is.EqualTo(Mathf.PI).Within(0.001f));
    }
}
