using UnityEngine;

/// <summary>
/// Pure-static RK4 numeric integrator for objects actively maneuvering under combined
/// multi-body gravity plus thrust. Pattern: SimulationMath.cs - a static math class with no
/// MonoBehaviour. Coasting objects never call into this - they use the cheaper closed-form
/// <see cref="OrbitalElements"/> propagation instead.
/// </summary>
public static class OrbitalNumericIntegrator
{
    /// <summary>Sums Newtonian gravitational acceleration (km/s^2) from every registered GravitySource at the given position.</summary>
    public static Vector3 GravityAccelerationKm(Vector3 posKm)
    {
        Vector3 totalAccel = Vector3.zero;
        var sources = GravitySource.All;

        for (int i = 0; i < sources.Count; i++)
        {
            GravitySource source = sources[i];
            Vector3 toBody = source.PositionKm() - posKm;
            float distance = toBody.magnitude;
            if (distance < 0.0001f)
                continue;

            float mu = (float)source.MuKm3PerSec2;
            totalAccel += toBody.normalized * (mu / (distance * distance));
        }

        return totalAccel;
    }

    /// <summary>Advances position/velocity (km, km/s) by one classic 4-stage Runge-Kutta step under gravity plus a constant thrust acceleration.</summary>
    public static void RK4Step(ref Vector3 posKm, ref Vector3 velKmPerSec, Vector3 thrustAccelKmPerSec2, double dtSeconds)
    {
        float dt = (float)dtSeconds;

        Vector3 k1Vel = GravityAccelerationKm(posKm) + thrustAccelKmPerSec2;
        Vector3 k1Pos = velKmPerSec;

        Vector3 k2Vel = GravityAccelerationKm(posKm + k1Pos * (dt * 0.5f)) + thrustAccelKmPerSec2;
        Vector3 k2Pos = velKmPerSec + k1Vel * (dt * 0.5f);

        Vector3 k3Vel = GravityAccelerationKm(posKm + k2Pos * (dt * 0.5f)) + thrustAccelKmPerSec2;
        Vector3 k3Pos = velKmPerSec + k2Vel * (dt * 0.5f);

        Vector3 k4Vel = GravityAccelerationKm(posKm + k3Pos * dt) + thrustAccelKmPerSec2;
        Vector3 k4Pos = velKmPerSec + k3Vel * dt;

        posKm += (dt / 6f) * (k1Pos + 2f * k2Pos + 2f * k3Pos + k4Pos);
        velKmPerSec += (dt / 6f) * (k1Vel + 2f * k2Vel + 2f * k3Vel + k4Vel);
    }
}
