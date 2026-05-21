using UnityEngine;

// Static class. Called once per nation per monthly strategic tick.
// Nations build own sites or rent, then queue launches (probe/crewed/infrastructure).
// 10 infrastructure points = 1 space station.
public static class LaunchSiteSystem
{
    const float BuildCost         = 50f;  // billions to build own site
    const float RentCostPerLaunch = 2f;   // billions added per rental launch
    const float ProbeCost         = 3f;   // billions
    const float CrewedCost        = 10f;  // billions
    const float InfraCost         = 8f;   // billions per infrastructure point
    const int   BuildTechMin      = 30;
    const int   InfraPerStation   = 10;

    public static void ProcessMonthlyDecision(NationRuntime n, int stageIdx)
    {
        if (stageIdx < 1) return; // Stage 1 (Undetected) — only major economies launch

        // Build own launch site if affordable and eligible
        if (n.launchSitesOwned == 0
            && n.techLevel >= BuildTechMin
            && n.treasury >= BuildCost
            && n.gdpBillions >= 200f)
        {
            n.treasury -= BuildCost;
            n.launchSitesOwned++;
            Debug.Log($"[LaunchSite] {n.iso3} built a launch site. Treasury: ${n.treasury:F0}B");
        }

        // Determine access and base costs
        bool  canOwnLaunch = n.launchSitesOwned > 0;
        bool  canRent      = n.treasury >= (ProbeCost + RentCostPerLaunch) && n.gdpBillions >= 50f;
        if (!canOwnLaunch && !canRent) return;

        // Choose payload — crewed > infra > probe, by priority
        if (canOwnLaunch && n.treasury >= CrewedCost && n.techLevel >= 50f)
        {
            n.treasury -= CrewedCost;
            n.totalLaunches++;
            Debug.Log($"[LaunchSite] {n.iso3} — crewed mission launched.");
        }
        else if (n.treasury >= (canOwnLaunch ? InfraCost : InfraCost + RentCostPerLaunch))
        {
            n.treasury      -= canOwnLaunch ? InfraCost : InfraCost + RentCostPerLaunch;
            n.totalLaunches++;
            n.infrastructurePoints++;
            if (n.infrastructurePoints >= InfraPerStation)
            {
                n.infrastructurePoints -= InfraPerStation;
                n.spaceStations++;
                Debug.Log($"[LaunchSite] {n.iso3} completed a space station! Total: {n.spaceStations}");
            }
        }
        else if (n.treasury >= (canOwnLaunch ? ProbeCost : ProbeCost + RentCostPerLaunch))
        {
            n.treasury -= canOwnLaunch ? ProbeCost : ProbeCost + RentCostPerLaunch;
            n.totalLaunches++;
            Debug.Log($"[LaunchSite] {n.iso3} — probe launched.");
        }
    }
}
