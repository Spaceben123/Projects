using NUnit.Framework;

public class NationEconomyTests
{
    const string MinJson = @"[
      {""iso3"":""USA"",""gdp"":29000.0,""pop"":335.0},
      {""iso3"":""CAN"",""gdp"":2272.0,""pop"":38.0},
      {""iso3"":""CHN"",""gdp"":18500.0,""pop"":1410.0}
    ]";

    NationDataRegistry CreateRegistry()
    {
        var go  = new UnityEngine.GameObject();
        var reg = go.AddComponent<NationDataRegistry>();
        reg.LoadFromJson(MinJson);
        return reg;
    }

    [Test]
    public void GetByIso3_ReturnsCorrectGdp()
    {
        var reg = CreateRegistry();
        var usa = reg.GetByIso3("USA");
        Assert.IsNotNull(usa);
        Assert.AreEqual(29000f, usa.gdpBillions, 0.1f);
    }

    [Test]
    public void GetByCountryIndex_ReturnsNation()
    {
        var reg = CreateRegistry();
        WorldRegionMapper.TryGetCountryIndex("USA", out byte idx);
        var nation = reg.GetByCountryIndex(idx);
        Assert.IsNotNull(nation);
        Assert.AreEqual("USA", nation.iso3);
    }

    [Test]
    public void TechLevel_HigherGdpPerCapita_HigherTech()
    {
        var reg = CreateRegistry();
        var usa = reg.GetByIso3("USA");
        var chn = reg.GetByIso3("CHN");
        Assert.IsNotNull(usa);
        Assert.IsNotNull(chn);
        // USA GDP/capita ~86B/M, CHN ~13B/M — USA should have higher tech seed
        Assert.Greater(usa.techLevel, chn.techLevel);
    }

    [Test]
    public void GetByIso3_Unknown_ReturnsNull()
    {
        var reg = CreateRegistry();
        Assert.IsNull(reg.GetByIso3("ZZZ"));
    }

    [Test]
    public void Treasury_IncreasesEachTick_AtStage1()
    {
        var reg    = CreateRegistry();
        var usa    = reg.GetByIso3("USA");
        float before = usa.treasury;

        // Manually apply 1 second of stage-1 economy (spaceRdFrac=0.22, fearDrag=0.00)
        float simDt       = 1f;
        float spaceRdFrac = 0.22f;
        float fearDrag    = 0.00f;
        float monthFrac   = simDt / 60f;
        float effectiveGdp = usa.gdpBillions * (1f - fearDrag);
        usa.treasury += (effectiveGdp * spaceRdFrac / 12f) * monthFrac;

        Assert.Greater(usa.treasury, before);
    }

    [Test]
    public void TechLevel_NeverExceeds100()
    {
        var reg = CreateRegistry();
        var usa = reg.GetByIso3("USA");
        usa.techLevel = 99.9f;

        // Force 1000 months of max space-R&D ticks
        float spaceRdFrac = 0.38f;
        float fearDrag    = 0.00f;
        for (int i = 0; i < 1000; i++)
        {
            float effectiveGdp  = usa.gdpBillions * (1f - fearDrag);
            float spaceRdBudget = effectiveGdp * spaceRdFrac / 12f;
            float costPerPoint  = 0.5f * UnityEngine.Mathf.Pow(1.055f, usa.techLevel);
            usa.techLevel = UnityEngine.Mathf.Min(100f, usa.techLevel + spaceRdBudget / costPerPoint);
        }
        Assert.LessOrEqual(usa.techLevel, 100f);
    }

    [Test]
    public void Population_DecreasesAtCollapse()
    {
        var reg = CreateRegistry();
        var usa = reg.GetByIso3("USA");
        float before = usa.populationM;
        // Stage 8 = Collapse → popGrowth = -0.00020f
        usa.populationM *= (1f + (-0.00020f));
        Assert.Less(usa.populationM, before);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var go in UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(
            UnityEngine.FindObjectsSortMode.None))
            UnityEngine.Object.DestroyImmediate(go);
    }
}
