using UnityEngine;

/// <summary>
/// Plain serializable propellant/mass-budget data owned by an <see cref="OrbitingBody"/>, not
/// its own MonoBehaviour. Pattern: SurfaceSite.cs - plain data owned and ticked by an owning
/// component rather than existing as a standalone object in the scene.
/// </summary>
[System.Serializable]
public class SpacecraftPropellant
{
    [SerializeField] double _dryMassKg = 1000.0;
    [SerializeField] double _propellantMassKg = 500.0;
    [SerializeField] double _specificImpulseSeconds = 300.0;

    public double DryMassKg => _dryMassKg;
    public double PropellantMassKg => _propellantMassKg;
    public double SpecificImpulseSeconds => _specificImpulseSeconds;

    /// <summary>Total current mass (dry + remaining propellant), in kilograms.</summary>
    public double CurrentMassKg => _dryMassKg + _propellantMassKg;

    /// <summary>
    /// Attempts to consume the propellant required for an instantaneous delta-v burn of the
    /// given magnitude, using the Tsiolkovsky rocket equation (deltaV = Isp * g0 * ln(m0/m1))
    /// solved for the post-burn mass. Returns false and consumes nothing if the required
    /// propellant exceeds what remains.
    /// </summary>
    public bool TryConsumeImpulsiveDeltaV(double deltaVKmPerSec)
    {
        double exhaustVelocityKmPerSec = _specificImpulseSeconds * OrbitalConstants.StandardGravityMPerSec2 / 1000.0;
        double massRatio = System.Math.Exp(deltaVKmPerSec / exhaustVelocityKmPerSec);
        double preBurnMassKg = CurrentMassKg;
        double postBurnMassKg = preBurnMassKg / massRatio;
        double requiredPropellantKg = preBurnMassKg - postBurnMassKg;

        if (requiredPropellantKg > _propellantMassKg)
            return false;

        _propellantMassKg -= requiredPropellantKg;
        return true;
    }

    /// <summary>Mass flow rate (kg/s) for a continuous burn producing the given thrust (Newtons).</summary>
    public double MassFlowRateKgPerSec(double thrustNewtons)
    {
        double exhaustVelocityMPerSec = _specificImpulseSeconds * OrbitalConstants.StandardGravityMPerSec2;
        return thrustNewtons / exhaustVelocityMPerSec;
    }

    /// <summary>Depletes propellant mass by the given amount over an elapsed time step, clamped to zero.</summary>
    public void ConsumeMass(double massKg)
    {
        _propellantMassKg = System.Math.Max(0.0, _propellantMassKg - massKg);
    }
}
