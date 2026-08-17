using System;
using UnityEngine;

// Single source of truth for "which country currently owns which DISTRICT" and
// "what alliance each country currently holds".
//
// Ownership is stored per district, never per country: conquering territory is a
// single integer write (SetDistrictOwner) and a national border is an emergent
// property of ownership — the set of district edges whose two sides have
// different owners. No vector geometry is ever mutated. This is the province
// model used by EU4/HOI4/Victoria.
//
// FactionTextureRenderer (fill) and CountryBorderRenderer (border lines) both
// subscribe to OnTerritoryChanged and independently re-derive their own
// colors/visibility here — this class never touches rendering directly, so any
// future gameplay system (contact-stage escalation, surface/space combat
// outcomes, an AI diplomacy pass) only ever needs to call the mutators below.
//
// The country-level API (SetController/ResetController/GetController) is
// preserved with widened semantics so existing callers keep working: annexing a
// whole nation now means "set every one of its districts' owners".
// Pattern follows RegionRegistry/NationDataRegistry: a singleton Instance with
// array-backed lookup.
public class TerritoryController : MonoBehaviour
{
    public static TerritoryController Instance { get; private set; }

    const int  kCountryCount = 256;
    const byte kNoCountry    = 255;

    // Per DISTRICT index: the byte country index currently owning it. Initialised
    // to each district's own parent country. Only district IDENTITY is 16-bit —
    // an owner is always a country, so this stays a byte array.
    byte[] _districtOwner = Array.Empty<byte>();

    // Alignment overrides stay COUNTRY-indexed: alignment is a national property,
    // not a per-district one.
    FactionAlignment[] _alignmentOverride    = new FactionAlignment[kCountryCount];
    bool[]             _hasAlignmentOverride = new bool[kCountryCount];

    bool _dirty;

    /// <summary>Fired once per frame (batched) after any ownership or alignment change, however many districts moved.</summary>
    public event Action OnTerritoryChanged;

    /// <summary>Number of districts under management. 0 when the project has not been baked.</summary>
    public int DistrictCount => _districtOwner.Length;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        _alignmentOverride    = new FactionAlignment[kCountryCount];
        _hasAlignmentOverride = new bool[kCountryCount];

        int count = WorldDistricts.Count;
        _districtOwner = new byte[count];
        for (int d = 0; d < count; d++)
            _districtOwner[d] = WorldDistricts.GetParentCountry((ushort)d);

        if (count == 0)
            Debug.LogWarning("[TerritoryController] No districts loaded — ownership changes will be inert. " +
                             "Run Assets > SpaceGame > Bake Region Map from Shapefile.");
    }

    void LateUpdate()
    {
        if (!_dirty) return;
        _dirty = false;
        OnTerritoryChanged?.Invoke();
    }

    // ---------------------------------------------------------------------
    // District-level ownership — the primitive everything else builds on
    // ---------------------------------------------------------------------

    /// <summary>Transfers a single district to owningCountryIdx. The border around it redraws on the next recolour pass.</summary>
    public void SetDistrictOwner(ushort districtIdx, byte owningCountryIdx)
    {
        if (districtIdx >= _districtOwner.Length) return;
        if (_districtOwner[districtIdx] == owningCountryIdx) return;
        _districtOwner[districtIdx] = owningCountryIdx;
        _dirty = true;
    }

    /// <summary>Restores a district to its original parent country (undoes any conquest).</summary>
    public void ResetDistrictOwner(ushort districtIdx)
    {
        if (districtIdx >= _districtOwner.Length) return;
        byte original = WorldDistricts.GetParentCountry(districtIdx);
        if (_districtOwner[districtIdx] == original) return;
        _districtOwner[districtIdx] = original;
        _dirty = true;
    }

    /// <summary>Country index currently owning a district, or 255 for an unknown/unloaded district.</summary>
    public byte GetDistrictOwner(ushort districtIdx)
        => districtIdx < _districtOwner.Length ? _districtOwner[districtIdx] : kNoCountry;

    /// <summary>Alliance colour/alignment that should be shown for a district, resolved through its current owner.</summary>
    public FactionAlignment GetDistrictAlignment(ushort districtIdx)
        => AlignmentOfCountry(GetDistrictOwner(districtIdx));

    // ---------------------------------------------------------------------
    // Country-level API — preserved, semantics widened to "all its districts"
    // ---------------------------------------------------------------------

    /// <summary>Marks every district of countryIdx as owned by controllingCountryIdx (whole-nation annexation).</summary>
    public void SetController(byte countryIdx, byte controllingCountryIdx)
    {
        var districts = WorldDistricts.DistrictsOfCountry(countryIdx);
        for (int i = 0; i < districts.Count; i++)
            SetDistrictOwner(districts[i], controllingCountryIdx);
    }

    /// <summary>Restores every district of countryIdx to self-control (undoes any annexation).</summary>
    public void ResetController(byte countryIdx)
    {
        var districts = WorldDistricts.DistrictsOfCountry(countryIdx);
        for (int i = 0; i < districts.Count; i++)
            ResetDistrictOwner(districts[i]);
    }

    /// <summary>
    /// Controlling country index for countryIdx: the unanimous owner of all its
    /// districts, or countryIdx itself when ownership is split — a partially
    /// conquered nation still exists.
    /// </summary>
    public byte GetController(byte countryIdx)
    {
        var districts = WorldDistricts.DistrictsOfCountry(countryIdx);
        if (districts.Count == 0) return countryIdx;

        byte first = GetDistrictOwner(districts[0]);
        for (int i = 1; i < districts.Count; i++)
            if (GetDistrictOwner(districts[i]) != first) return countryIdx;

        return first == kNoCountry ? countryIdx : first;
    }

    /// <summary>Overrides countryIdx's own alliance (e.g. a defection), independent of its macro-region default.</summary>
    public void SetAlignment(byte countryIdx, FactionAlignment alignment)
    {
        if (_hasAlignmentOverride[countryIdx] && _alignmentOverride[countryIdx] == alignment) return;
        _alignmentOverride[countryIdx]    = alignment;
        _hasAlignmentOverride[countryIdx] = true;
        _dirty = true;
    }

    /// <summary>
    /// Returns the current alignment that should be shown for countryIdx, resolved
    /// through its controlling country.
    /// </summary>
    public FactionAlignment GetAlignment(byte countryIdx) => AlignmentOfCountry(GetController(countryIdx));

    /// <summary>
    /// Raw alignment of one country: its explicit override if set, else its
    /// macro-region default from RegionRegistry (looked up live, so region-wide
    /// events like ContactStageManager's collapse/merge are reflected without
    /// this class needing its own subscription to them).
    /// </summary>
    FactionAlignment AlignmentOfCountry(byte countryIdx)
    {
        // No bounds check needed: a byte can never index past the 256-entry arrays.
        if (_hasAlignmentOverride[countryIdx])
            return _alignmentOverride[countryIdx];

        byte regionIdx = WorldRegionMapper.GetRegionForCountry(countryIdx);
        var  registry  = RegionRegistry.Instance;
        if (registry != null && regionIdx < registry.Regions.Length && registry.Regions[regionIdx] != null)
            return registry.Regions[regionIdx].Alignment;

        return FactionAlignment.NonAligned;
    }
}
