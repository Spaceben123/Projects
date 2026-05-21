using UnityEngine;

public enum FactionAlignment { NATO = 0, BRICS = 1, NonAligned = 2, SuperNation = 3, Collapsed = 4 }

public class RegionRuntime
{
    public RegionDefinition Def { get; }

    public FactionAlignment Alignment;
    public float DamageLevel;
    public float PopulationM;
    public float GdpTrillion;
    public float TechLevel;
    public bool  PowerGridOnline = true;

    public RegionRuntime(RegionDefinition def)
    {
        Def         = def;
        Alignment   = (FactionAlignment)def.defaultAlignment;
        PopulationM = def.startingPopulationM;
        TechLevel   = 0.65f;
        GdpTrillion = SimulationMath.CalcGDP(PopulationM, def.baseWealth, TechLevel, 1, 0f);
    }

    public void MergeToSuperNation() => Alignment = FactionAlignment.SuperNation;
    public void Collapse()           => Alignment = FactionAlignment.Collapsed;
    public void RestoreAlignment()   => Alignment = (FactionAlignment)Def.defaultAlignment;
}
