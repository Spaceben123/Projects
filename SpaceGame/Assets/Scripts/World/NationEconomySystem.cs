using UnityEngine;

// Drives per-nation economy each frame. Call Tick(deltaTime, timeWarpFactor) from WorldSimulation.Update.
// Economy accumulates continuously — no lump-sum monthly payouts.
public class NationEconomySystem : MonoBehaviour
{
    // GDP allocation fractions per ContactStage (index = (int)stage - 1, stages 1-8)
    static readonly float[] s_spaceRdFrac  = { 0.22f, 0.26f, 0.28f, 0.30f, 0.28f, 0.38f, 0.20f, 0.10f };
    static readonly float[] s_fearDragFrac = { 0.00f, 0.02f, 0.07f, 0.10f, 0.14f, 0.15f, 0.30f, 0.40f };
    static readonly float[] s_civFrac      = { 0.70f, 0.62f, 0.50f, 0.42f, 0.30f, 0.22f, 0.15f, 0.10f };

    // Population growth per month by stage
    static readonly float[] s_popGrowthPerMonth = { 0.00070f, 0.00060f, 0.00030f, 0.00020f,
                                                     0.00010f, 0.00000f,-0.00010f,-0.00020f };

    // Tech cost per point scales exponentially with current tech
    const float TechBaseCost = 0.5f;   // billions per tech point at tech=0
    const float TechCostExp  = 1.055f; // multiplier per existing tech level

    // Seconds per game-month (must match WorldSimulation.SecsPerSimMonth = 60f)
    const float SecsPerMonth = 60f;

    ContactStageManager _stages;

    void Start() => _stages = ContactStageManager.Instance;

    public void Tick(float deltaTime, float timeWarpFactor)
    {
        if (NationDataRegistry.Instance == null) return;

        float simDt    = deltaTime * timeWarpFactor;
        int   stageIdx = _stages != null ? Mathf.Clamp((int)_stages.CurrentStage - 1, 0, 7) : 0;

        float spaceRdFrac  = s_spaceRdFrac[stageIdx];
        float fearDragFrac = s_fearDragFrac[stageIdx];
        float popGrowth    = s_popGrowthPerMonth[stageIdx];

        var nations = NationDataRegistry.Instance.All;
        foreach (var nation in nations)
        {
            if (nation == null) continue;
            TickNation(nation, simDt, spaceRdFrac, fearDragFrac, popGrowth, stageIdx);
        }
    }

    void TickNation(NationRuntime n, float simDt,
                    float spaceRdFrac, float fearDragFrac, float popGrowth, int stageIdx)
    {
        float monthFraction = simDt / SecsPerMonth;

        // Effective GDP after fear drag
        float effectiveGdp = n.gdpBillions * (1f - fearDragFrac);

        // Treasury drip: Space R&D budget flows in continuously
        float spaceRdMonthly = effectiveGdp * spaceRdFrac / 12f;
        n.treasury += spaceRdMonthly * monthFraction;

        // Accumulate for monthly strategic tick
        n.accumulatedMonths += monthFraction;
        if (n.accumulatedMonths >= 1f)
        {
            RunMonthlyTick(n, popGrowth, spaceRdFrac, fearDragFrac, stageIdx);
            n.accumulatedMonths -= 1f;
        }
    }

    void RunMonthlyTick(NationRuntime n, float popGrowth,
                        float spaceRdFrac, float fearDragFrac, int stageIdx)
    {
        // Population growth
        n.populationM *= (1f + popGrowth);

        // GDP growth: driven by civilian + space fractions × tech multiplier
        float effectiveGdp  = n.gdpBillions * (1f - fearDragFrac);
        float civFrac       = s_civFrac[stageIdx];
        float gdpGrowthRate = 0.003f + (n.techLevel / 100f) * 0.007f;
        n.gdpBillions      *= 1f + gdpGrowthRate * (civFrac + spaceRdFrac);

        // Tech advancement — diminishing returns
        float spaceRdBudget = effectiveGdp * spaceRdFrac / 12f;
        float costPerPoint  = TechBaseCost * Mathf.Pow(TechCostExp, n.techLevel);
        if (costPerPoint > 0f)
            n.techLevel = Mathf.Min(100f, n.techLevel + spaceRdBudget / costPerPoint);

        LaunchSiteSystem.ProcessMonthlyDecision(n, stageIdx);
    }
}
