using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Creates a stat panel in the top-left corner. Attach to any persistent GameObject.
// Assign the NationSelectionSystem reference in Inspector, or it auto-discovers at Start.
public class NationStatPanel : MonoBehaviour
{
    [SerializeField] NationSelectionSystem _selectionSystem;

    Canvas          _canvas;
    GameObject      _panel;
    TextMeshProUGUI _text;

    void Awake()
    {
        BuildUI();
    }

    void Start()
    {
        if (_selectionSystem == null)
            _selectionSystem = FindFirstObjectByType<NationSelectionSystem>();

        if (_selectionSystem != null)
        {
            _selectionSystem.OnNationSelected   += Show;
            _selectionSystem.OnNationDeselected += Hide;
        }

        Hide();
    }

    void OnDestroy()
    {
        if (_selectionSystem != null)
        {
            _selectionSystem.OnNationSelected   -= Show;
            _selectionSystem.OnNationDeselected -= Hide;
        }
    }

    void BuildUI()
    {
        // Canvas
        var canvasGo = new GameObject("NationStatCanvas");
        canvasGo.transform.SetParent(transform);
        _canvas              = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 10;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        // Panel background
        _panel = new GameObject("StatPanel");
        _panel.transform.SetParent(canvasGo.transform, false);
        var panelRect              = _panel.AddComponent<RectTransform>();
        panelRect.anchorMin        = new Vector2(0, 1);
        panelRect.anchorMax        = new Vector2(0, 1);
        panelRect.pivot            = new Vector2(0, 1);
        panelRect.anchoredPosition = new Vector2(16, -16);
        panelRect.sizeDelta        = new Vector2(280, 340);

        var bg   = _panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.72f);

        // Text
        var textGo = new GameObject("StatText");
        textGo.transform.SetParent(_panel.transform, false);
        var textRect          = textGo.AddComponent<RectTransform>();
        textRect.anchorMin    = Vector2.zero;
        textRect.anchorMax    = Vector2.one;
        textRect.offsetMin    = new Vector2(12, 12);
        textRect.offsetMax    = new Vector2(-12, -12);
        _text                 = textGo.AddComponent<TextMeshProUGUI>();
        _text.fontSize        = 13f;
        _text.color           = Color.white;
        _text.alignment       = TextAlignmentOptions.TopLeft;
        _text.enableWordWrapping = false;
    }

    public void Show(NationRuntime nation)
    {
        if (nation == null) { Hide(); return; }

        var regionByte = WorldRegionMapper.GetRegionForCountry(nation.countryIdx);
        string regionName = GetRegionDisplayName(regionByte);

        string stationStr = nation.spaceStations > 0 ? $"{nation.spaceStations}" : "—";
        string sitesStr   = nation.launchSitesOwned > 0 ? $"{nation.launchSitesOwned} owned" : "renting";
        int    barFilled  = Mathf.RoundToInt(nation.techLevel / 10f);
        string techBar    = new string('█', barFilled) + new string('░', 10 - barFilled);

        _text.text =
            $"<b>{nation.iso3}</b>   [{regionName}]\n" +
            $"─────────────────────\n" +
            $"GDP        ${FormatBillions(nation.gdpBillions)}\n" +
            $"Population {nation.populationM:F1}M\n" +
            $"Tech  {techBar} {nation.techLevel:F0}\n" +
            $"Treasury  ${FormatBillions(nation.treasury)}\n" +
            $"─────────────────────\n" +
            $"Launch sites  {sitesStr}\n" +
            $"Total launches  {nation.totalLaunches}\n" +
            $"Infra points  {nation.infrastructurePoints}\n" +
            $"Space stations  {stationStr}\n" +
            $"─────────────────────\n" +
            $"[ESC or click to deselect]";

        _panel.SetActive(true);
    }

    public void Hide()
    {
        if (_panel != null) _panel.SetActive(false);
    }

    static string FormatBillions(float b)
    {
        if (b >= 1000f) return $"{b / 1000f:F1}T";
        return $"{b:F0}B";
    }

    static string GetRegionDisplayName(byte idx) => idx switch
    {
        0  => "N.America",
        1  => "C.America",
        2  => "S.America",
        3  => "W.Europe",
        4  => "E.Europe",
        5  => "Russia",
        6  => "Middle East",
        7  => "N.Africa",
        8  => "S.Africa",
        9  => "E.Asia",
        10 => "S.Asia",
        11 => "SE.Asia",
        12 => "C.Asia",
        13 => "Oceania",
        _  => "Unknown"
    };
}
