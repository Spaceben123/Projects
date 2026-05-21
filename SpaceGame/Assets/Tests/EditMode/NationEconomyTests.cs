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

    [TearDown]
    public void TearDown()
    {
        foreach (var go in UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(
            UnityEngine.FindObjectsSortMode.None))
            UnityEngine.Object.DestroyImmediate(go);
    }
}
