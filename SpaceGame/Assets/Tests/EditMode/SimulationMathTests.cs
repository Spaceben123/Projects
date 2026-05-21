using NUnit.Framework;

public class SimulationMathTests
{
    [Test]
    public void PopGrowthRate_Stage1_FullDamage_ReturnsZero()
    {
        float rate = SimulationMath.PopGrowthRatePerYear(1.0f, 1, 1.0f);
        Assert.That(rate, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void PopGrowthRate_Stage1_NoDamage_ModernTech_ReturnsPositive()
    {
        float rate = SimulationMath.PopGrowthRatePerYear(1.0f, 1, 0f);
        Assert.Greater(rate, 0f);
    }

    [Test]
    public void PopGrowthRate_CollapseStage_IsNegative()
    {
        // ContactStage.Collapse = 8 (enum starts at 1), s_popStageFactor[8] = -0.50f
        float rate = SimulationMath.PopGrowthRatePerYear(0.5f, 8, 0f);
        Assert.Less(rate, 0f);
    }

    [Test]
    public void GDP_ZeroPopulation_ReturnsZero()
    {
        float gdp = SimulationMath.CalcGDP(0f, 0.5f, 0.5f, 1, 0f);
        Assert.That(gdp, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void GDP_FullDamage_NearlyZero()
    {
        float gdp = SimulationMath.CalcGDP(500f, 0.85f, 1.0f, 1, 1.0f);
        Assert.That(gdp, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void GDP_Stage6_LowerThanStage1()
    {
        float gdp1 = SimulationMath.CalcGDP(500f, 0.85f, 0.5f, 1, 0f);
        float gdp6 = SimulationMath.CalcGDP(500f, 0.85f, 0.5f, 6, 0f);
        Assert.Greater(gdp1, gdp6);
    }

    [Test]
    public void TechAdvance_CollapseStage_ReturnsZero()
    {
        // ContactStage.Collapse = 8, s_techStageFactor[8] = 0.00f
        float r = SimulationMath.TechAdvanceRate(0.1f, 1.0f, 8, 0f);
        Assert.That(r, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void FearDelta_HighSpectacle_HigherThanLow()
    {
        float low  = SimulationMath.FearDelta(0.1f, false, 0.2f, 1);
        float high = SimulationMath.FearDelta(0.1f, true,  0.8f, 1);
        Assert.Greater(high, low);
    }

    [Test]
    public void FearDelta_Repetition_Doubles()
    {
        float once  = SimulationMath.FearDelta(0.1f, false, 0.5f, 1);
        float twice = SimulationMath.FearDelta(0.1f, false, 0.5f, 2);
        Assert.That(twice, Is.EqualTo(once * 2f).Within(0.001f));
    }
}
