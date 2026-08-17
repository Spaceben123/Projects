// The starter set of non-physical "things happening on the surface" a nation
// can have. What each type actually costs/produces is a game-design decision
// left for a later pass — see SurfaceSiteRegistry.Tick.
public enum SurfaceSiteType { Outpost, Industry, Defense }

// Plain runtime data for one non-physical "thing happening on the surface" —
// a nation's outpost/construction site that acts and changes over time.
// No MonoBehaviour: owned and ticked by SurfaceSiteRegistry, same pattern as
// NationRuntime is owned and ticked by NationDataRegistry/NationEconomySystem.
[System.Serializable]
public class SurfaceSite
{
    public byte            ownerCountryIdx;
    public float            lat;
    public float            lon;
    public SurfaceSiteType type;
    public float            progress01; // 0-1 current build/upgrade progress
    public int              level;      // completed upgrade tiers
}
