// Per-nation mutable simulation state. Plain C# class — no MonoBehaviour.
[System.Serializable]
public class NationRuntime
{
    public string iso3;
    public byte   countryIdx;       // WorldRegionMapper alphabetical index

    // Seeded from JSON
    public float gdpBillions;       // nominal GDP, grows each month
    public float populationM;       // population in millions

    // Derived at registry load
    public float techLevel;         // 0–100, seeded from GDP/capita rank

    // Simulation state
    public float treasury;          // accumulated unspent Space R&D budget (billions)
    public float accumulatedMonths; // deltaTime fraction accumulator

    // Launch site state
    public int   launchSitesOwned;
    public int   totalLaunches;
    public int   infrastructurePoints;
    public int   spaceStations;
}
