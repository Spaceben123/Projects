using UnityEngine;

public static class SimulationMath
{
    // Index 0 unused. Indices 1-8 map to ContactStage int values 1-8.
    static readonly float[] s_popStageFactor  = { 0f, 1.0f, 1.0f, 0.85f, 0.75f, 0.75f, 0.50f, -0.50f, -0.50f };
    static readonly float[] s_gdpStageFactor  = { 0f, 1.0f, 1.0f, 0.90f, 0.80f, 0.80f, 0.60f, 0.30f,  0.05f };
    static readonly float[] s_techStageFactor = { 0f, 1.0f, 1.0f, 1.30f, 1.60f, 1.60f, 1.60f, 0.00f,  0.00f };

    public static float PopGrowthRatePerYear(float techLevel, int stage, float damageLevel)
    {
        float baseRate = Mathf.Lerp(0.005f, 0.011f, techLevel);
        return baseRate * s_popStageFactor[stage] * (1f - damageLevel);
    }

    public static float CalcGDP(float populationM, float baseWealth, float techLevel, int stage, float damageLevel)
    {
        float perCapita    = Mathf.Lerp(5000f, 55000f, baseWealth);
        float techMult     = Mathf.Lerp(1.0f, 4.0f, techLevel);
        float damageFactor = (1f - damageLevel) * (1f - damageLevel);
        return populationM * 1e6f * perCapita * techMult * s_gdpStageFactor[stage] * damageFactor / 1e12f;
    }

    public static float TechAdvanceRate(float researchFraction, float gdp, int stage, float damageLevel)
    {
        float baseRate = researchFraction * gdp * 0.00002f;
        return baseRate * s_techStageFactor[stage] * (1f - damageLevel);
    }

    public static float FearDelta(float baseFearValue, bool witnessedByMillions, float populationDensity01, int repetitionCount)
    {
        float spectacle = witnessedByMillions ? 2.0f : 1.0f;
        float location  = 1.0f + populationDensity01 * 2.0f;
        return baseFearValue * spectacle * location * repetitionCount;
    }

    public const float FearDecayPerSec = 0.005f;

    public static float SpontaneousAttackProbPerSec(float hawkishness, float fearLevel, int repetitionMultiplier)
    {
        float base01 = Mathf.Lerp(0.00001f, 0.0001f, hawkishness);
        return base01 * fearLevel * repetitionMultiplier;
    }

    // Index 0 unused. Indices 1-8 correspond to ContactStage int values 1-8.
    // Stage rises when FearLevel exceeds this threshold.
    public static readonly float[] StageThresholds = { 0f, 0f, 0.05f, 0.20f, 0.40f, 0.55f, 0.72f, 0.90f, 0f };
}
