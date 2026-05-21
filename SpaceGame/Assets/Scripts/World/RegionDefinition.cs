using System;
using UnityEngine;

[Serializable]
public class RegionDefinition
{
    public string   regionId;
    public string   displayName;
    public int      defaultAlignment;   // 0=NATO, 1=BRICS, 2=NonAligned
    public bool     isNuclearPower;
    public float    baseWealth;
    public float    hawkishness;
    public float    capitalLat;
    public float    capitalLon;
    public float    startingPopulationM;
    public float[]  boundary;           // flat lat/lon pairs alternating
}

[Serializable]
public class RegionDefinitionList
{
    public RegionDefinition[] regions;
}
