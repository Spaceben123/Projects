using UnityEngine;

/// <summary>
/// Classical (Keplerian) orbital elements describing a coasting orbit around a single primary
/// body. Propagation is closed-form (Kepler's equation solved via Newton-Raphson), which is why
/// a coasting object can go "static" between updates instead of paying for numeric integration
/// every frame - only <see cref="OrbitingBody.OrbitalState.Maneuvering"/> objects use
/// <see cref="OrbitalNumericIntegrator"/> instead.
///
/// All elements are stored as double: mean-anomaly accumulation over long simulated spans
/// (years, at up to 3650x time warp) would drift visibly in float. The polar/reference axis
/// convention here matches this project's world space directly - world +Y is the primary
/// body's spin/inclination-reference axis (in place of the traditional astrodynamics +Z), and
/// world +X is the zero-longitude/RAAN reference direction. Because the underlying vector
/// algebra (cross/dot products) is coordinate-frame agnostic, standard orbital-mechanics
/// formulas apply unchanged under this relabeling.
/// </summary>
[System.Serializable]
public struct OrbitalElements
{
    public double SemiMajorAxisKm;
    public double Eccentricity;
    public double InclinationRad;
    public double RaanRad;
    public double ArgOfPeriapsisRad;
    public double MeanAnomalyAtEpochRad;
    public double EpochSeconds;

    const int MaxKeplerIterations = 8;
    const double MinVectorMagnitude = 1e-9;

    /// <summary>Derives classical elements from a Cartesian state vector (patched-conic approximation after a burn).</summary>
    public static OrbitalElements FromStateVectors(Vector3 posKm, Vector3 velKmPerSec, double muKm3PerSec2, double epochSeconds)
    {
        DVec3 r = posKm;
        DVec3 v = velKmPerSec;
        double mu = muKm3PerSec2;
        double rMag = r.Magnitude;
        double vMag = v.Magnitude;

        DVec3 h = DVec3.Cross(r, v);
        double hMag = h.Magnitude;

        DVec3 poleAxis = new DVec3(0.0, 1.0, 0.0);
        DVec3 nodeVec = DVec3.Cross(poleAxis, h);
        double nodeMag = nodeVec.Magnitude;

        DVec3 eccentricityVec = (r * (vMag * vMag - mu / rMag) - v * DVec3.Dot(r, v)) * (1.0 / mu);
        double eccentricity = eccentricityVec.Magnitude;

        double specificEnergy = vMag * vMag * 0.5 - mu / rMag;
        double semiMajorAxis = System.Math.Abs(specificEnergy) > MinVectorMagnitude ? -mu / (2.0 * specificEnergy) : rMag;

        double inclination = hMag > MinVectorMagnitude ? System.Math.Acos(Clamp(h.y / hMag, -1.0, 1.0)) : 0.0;

        double raan = 0.0;
        if (nodeMag > MinVectorMagnitude)
        {
            raan = System.Math.Acos(Clamp(nodeVec.x / nodeMag, -1.0, 1.0));
            if (nodeVec.z < 0.0) raan = 2.0 * System.Math.PI - raan;
        }

        double argOfPeriapsis = 0.0;
        if (nodeMag > MinVectorMagnitude && eccentricity > MinVectorMagnitude)
        {
            argOfPeriapsis = System.Math.Acos(Clamp(DVec3.Dot(nodeVec, eccentricityVec) / (nodeMag * eccentricity), -1.0, 1.0));
            if (eccentricityVec.y < 0.0) argOfPeriapsis = 2.0 * System.Math.PI - argOfPeriapsis;
        }

        double trueAnomaly;
        if (eccentricity > MinVectorMagnitude)
        {
            trueAnomaly = System.Math.Acos(Clamp(DVec3.Dot(eccentricityVec, r) / (eccentricity * rMag), -1.0, 1.0));
            if (DVec3.Dot(r, v) < 0.0) trueAnomaly = 2.0 * System.Math.PI - trueAnomaly;
        }
        else
        {
            // Circular orbit: eccentricity vector is undefined, fall back to measuring from +X.
            trueAnomaly = System.Math.Acos(Clamp(r.x / rMag, -1.0, 1.0));
            if (r.z < 0.0) trueAnomaly = 2.0 * System.Math.PI - trueAnomaly;
        }

        double eccentricAnomaly = 2.0 * System.Math.Atan2(
            System.Math.Sqrt(System.Math.Max(0.0, 1.0 - eccentricity)) * System.Math.Sin(trueAnomaly * 0.5),
            System.Math.Sqrt(System.Math.Max(0.0, 1.0 + eccentricity)) * System.Math.Cos(trueAnomaly * 0.5));
        double meanAnomaly = eccentricAnomaly - eccentricity * System.Math.Sin(eccentricAnomaly);

        return new OrbitalElements
        {
            SemiMajorAxisKm = semiMajorAxis,
            Eccentricity = eccentricity,
            InclinationRad = inclination,
            RaanRad = NormalizeAngle(raan),
            ArgOfPeriapsisRad = NormalizeAngle(argOfPeriapsis),
            MeanAnomalyAtEpochRad = NormalizeAngle(meanAnomaly),
            EpochSeconds = epochSeconds
        };
    }

    /// <summary>Solves Kepler's equation for the given time and converts the result back to a Cartesian state vector, in kilometers.</summary>
    public (Vector3 posKm, Vector3 velKmPerSec) ToStateVectors(double atEpochSeconds, double muKm3PerSec2)
    {
        double a = SemiMajorAxisKm;
        double e = Eccentricity;
        double mu = muKm3PerSec2;

        double meanMotion = System.Math.Sqrt(mu / (a * a * a));
        double meanAnomaly = NormalizeAngle(MeanAnomalyAtEpochRad + meanMotion * (atEpochSeconds - EpochSeconds));

        double eccentricAnomaly = e < 0.8 ? meanAnomaly : System.Math.PI;
        for (int i = 0; i < MaxKeplerIterations; i++)
        {
            double f = eccentricAnomaly - e * System.Math.Sin(eccentricAnomaly) - meanAnomaly;
            double fPrime = 1.0 - e * System.Math.Cos(eccentricAnomaly);
            eccentricAnomaly -= f / fPrime;
        }

        double trueAnomaly = 2.0 * System.Math.Atan2(
            System.Math.Sqrt(1.0 + e) * System.Math.Sin(eccentricAnomaly * 0.5),
            System.Math.Sqrt(1.0 - e) * System.Math.Cos(eccentricAnomaly * 0.5));
        double radiusKm = a * (1.0 - e * System.Math.Cos(eccentricAnomaly));
        double angularMomentumMag = System.Math.Sqrt(mu * a * (1.0 - e * e));

        DVec3 poleAxis = new DVec3(0.0, 1.0, 0.0);
        DVec3 nodeHat = new DVec3(System.Math.Cos(RaanRad), 0.0, System.Math.Sin(RaanRad));
        DVec3 orbitNormalHat = poleAxis * System.Math.Cos(InclinationRad) + DVec3.Cross(nodeHat, poleAxis) * System.Math.Sin(InclinationRad);
        DVec3 periapsisHat = nodeHat * System.Math.Cos(ArgOfPeriapsisRad) + DVec3.Cross(orbitNormalHat, nodeHat) * System.Math.Sin(ArgOfPeriapsisRad);
        DVec3 alongTrackHat = DVec3.Cross(orbitNormalHat, periapsisHat);

        double cosNu = System.Math.Cos(trueAnomaly);
        double sinNu = System.Math.Sin(trueAnomaly);

        DVec3 posKm = periapsisHat * (radiusKm * cosNu) + alongTrackHat * (radiusKm * sinNu);
        DVec3 velKmPerSec = periapsisHat * (-mu / angularMomentumMag * sinNu) + alongTrackHat * (mu / angularMomentumMag * (e + cosNu));

        return (posKm, velKmPerSec);
    }

    /// <summary>Full orbital period, in seconds, for the given primary body's gravitational parameter.</summary>
    public double OrbitalPeriodSeconds(double muKm3PerSec2)
    {
        return 2.0 * System.Math.PI * System.Math.Sqrt(SemiMajorAxisKm * SemiMajorAxisKm * SemiMajorAxisKm / muKm3PerSec2);
    }

    static double Clamp(double value, double min, double max)
    {
        return value < min ? min : (value > max ? max : value);
    }

    static double NormalizeAngle(double angleRad)
    {
        double twoPi = 2.0 * System.Math.PI;
        double normalized = angleRad % twoPi;
        return normalized < 0.0 ? normalized + twoPi : normalized;
    }

    /// <summary>Minimal double-precision 3D vector, used internally so Kepler-equation math never loses precision to Unity's float Vector3.</summary>
    struct DVec3
    {
        public double x, y, z;

        public DVec3(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public double Magnitude => System.Math.Sqrt(x * x + y * y + z * z);

        public static DVec3 operator +(DVec3 a, DVec3 b) => new DVec3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static DVec3 operator -(DVec3 a, DVec3 b) => new DVec3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static DVec3 operator *(DVec3 a, double s) => new DVec3(a.x * s, a.y * s, a.z * s);

        public static double Dot(DVec3 a, DVec3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public static DVec3 Cross(DVec3 a, DVec3 b) => new DVec3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x);

        public static implicit operator Vector3(DVec3 v) => new Vector3((float)v.x, (float)v.y, (float)v.z);
        public static implicit operator DVec3(Vector3 v) => new DVec3(v.x, v.y, v.z);
    }
}
