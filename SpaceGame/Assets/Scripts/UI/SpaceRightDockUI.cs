using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Eve Online-style docked UI in the screen's right corner: three toggle buttons (Information,
/// Ship Movement, Weapons/Abilities) that each pop open a flyout panel next to the dock. The
/// Information panel hosts a ground-track map (GroundTrackMapPanel) and the categorized
/// craft/satellite list (SpaceObjectListPanel); Ship Movement and Weapons are placeholder
/// shells reserved for follow-on work.
/// </summary>
public class SpaceRightDockUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] CameraController _cameraController;
    [SerializeField] Transform _earthTransform;
    [SerializeField] Renderer _earthRenderer;

    [Header("Layout")]
    [SerializeField] Vector2 _dockMargin = new Vector2(24f, 24f);
    [SerializeField] Vector2 _buttonSize = new Vector2(170f, 46f);
    [SerializeField] float _buttonSpacing = 8f;
    [SerializeField] float _panelGap = 16f;
    [SerializeField] float _openAnimationSeconds = 0.22f;
    [SerializeField] float _openPopOffsetPixels = 26f;

    static readonly Color DockButtonColor = new Color(0.08f, 0.11f, 0.16f, 0.92f);
    static readonly Color DockButtonHighlightColor = new Color(0.16f, 0.46f, 0.62f, 0.95f);
    static readonly Color PanelBackgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.92f);
    static readonly Color HeaderTextColor = new Color(0.7f, 0.86f, 0.94f, 1f);
    static readonly Color PlaceholderTextColor = new Color(0.75f, 0.8f, 0.86f, 0.85f);

    const string EarthDayTexturePropertyId = "_DayTex";

    readonly DockEntry[] _entries = new DockEntry[3];

    class DockEntry
    {
        public Button Button;
        public Image ButtonBackground;
        public RectTransform PanelRoot;
        public CanvasGroup PanelGroup;
        public bool IsOpen;
        public Coroutine ActiveAnimation;
        public Vector2 OpenAnchoredPosition;
    }

    void Awake()
    {
        if (_cameraController == null)
            _cameraController = FindAnyObjectByType<CameraController>();

        if (_earthTransform == null)
        {
            GameObject earthGo = GameObject.Find("Earth");
            if (earthGo != null)
                _earthTransform = earthGo.transform;
        }

        if (_earthRenderer == null && _earthTransform != null)
            _earthRenderer = _earthTransform.GetComponent<Renderer>();

        BuildUi();
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("SpaceRightDockCanvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        canvasGo.AddComponent<GraphicRaycaster>();

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Transform canvasRoot = canvasGo.transform;

        var dockGo = new GameObject("RightDock", typeof(RectTransform));
        dockGo.transform.SetParent(canvasRoot, false);
        var dockRoot = dockGo.GetComponent<RectTransform>();
        dockRoot.anchorMin = new Vector2(1f, 1f);
        dockRoot.anchorMax = new Vector2(1f, 1f);
        dockRoot.pivot = new Vector2(1f, 1f);
        dockRoot.anchoredPosition = new Vector2(-_dockMargin.x, -_dockMargin.y);
        dockRoot.sizeDelta = _buttonSize;

        var dockLayout = dockGo.AddComponent<VerticalLayoutGroup>();
        dockLayout.spacing = _buttonSpacing;
        dockLayout.childControlWidth = true;
        dockLayout.childControlHeight = true;
        dockLayout.childForceExpandWidth = true;
        dockLayout.childForceExpandHeight = true;

        float panelAnchoredX = -(_dockMargin.x + _buttonSize.x + _panelGap);
        float panelAnchoredY = -_dockMargin.y;

        _entries[0] = BuildDockEntry(dockGo.transform, canvasRoot, "INFORMATION", panelAnchoredX, panelAnchoredY, new Vector2(580f, 640f));
        BuildInformationPanelContent(_entries[0].PanelRoot);

        _entries[1] = BuildDockEntry(dockGo.transform, canvasRoot, "SHIP MOVEMENT", panelAnchoredX, panelAnchoredY, new Vector2(360f, 220f));
        BuildPlaceholderPanelContent(_entries[1].PanelRoot, "Ship movement controls\n(throttle, heading, delta-v burns)\ncoming soon.");

        _entries[2] = BuildDockEntry(dockGo.transform, canvasRoot, "WEAPONS", panelAnchoredX, panelAnchoredY, new Vector2(360f, 220f));
        BuildPlaceholderPanelContent(_entries[2].PanelRoot, "Weapons & abilities\n(missiles, countermeasures)\ncoming soon.");
    }

    DockEntry BuildDockEntry(Transform dockParent, Transform panelParent, string label, float panelAnchoredX, float panelAnchoredY, Vector2 panelSize)
    {
        var entry = new DockEntry();

        var buttonGo = new GameObject($"{label}Button", typeof(RectTransform));
        buttonGo.transform.SetParent(dockParent, false);
        buttonGo.AddComponent<LayoutElement>().preferredHeight = _buttonSize.y;

        var background = buttonGo.AddComponent<Image>();
        background.color = DockButtonColor;
        entry.ButtonBackground = background;

        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = background;
        entry.Button = button;

        TextMeshProUGUI buttonLabel = CreateText(buttonGo.transform, label, 13f, FontStyles.Bold, Color.white);
        buttonLabel.alignment = TextAlignmentOptions.Center;
        RectTransform buttonLabelRect = buttonLabel.rectTransform;
        buttonLabelRect.anchorMin = Vector2.zero;
        buttonLabelRect.anchorMax = Vector2.one;
        buttonLabelRect.offsetMin = Vector2.zero;
        buttonLabelRect.offsetMax = Vector2.zero;

        var panelGo = new GameObject($"{label}Panel", typeof(RectTransform));
        panelGo.transform.SetParent(panelParent, false);
        var panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.sizeDelta = panelSize;
        panelRect.anchoredPosition = new Vector2(panelAnchoredX, panelAnchoredY);

        var panelBackground = panelGo.AddComponent<Image>();
        panelBackground.color = PanelBackgroundColor;

        var panelGroup = panelGo.AddComponent<CanvasGroup>();
        panelGroup.alpha = 0f;
        panelGroup.blocksRaycasts = false;
        panelGroup.interactable = false;

        TextMeshProUGUI headerText = CreateText(panelGo.transform, label, 14f, FontStyles.Bold, HeaderTextColor);
        RectTransform headerRect = headerText.rectTransform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = new Vector2(0f, -12f);
        headerRect.sizeDelta = new Vector2(-24f, 20f);

        entry.PanelRoot = panelRect;
        entry.PanelGroup = panelGroup;
        entry.OpenAnchoredPosition = panelRect.anchoredPosition;

        panelGo.SetActive(false);

        button.onClick.AddListener(() => ToggleEntry(entry));

        return entry;
    }

    void BuildInformationPanelContent(RectTransform panelRoot)
    {
        Texture dayTexture = _earthRenderer != null && _earthRenderer.sharedMaterial != null
            ? _earthRenderer.sharedMaterial.GetTexture(EarthDayTexturePropertyId)
            : null;

        var mapPanel = panelRoot.gameObject.AddComponent<GroundTrackMapPanel>();
        Vector2 mapSize = new Vector2(536f, 268f);
        mapPanel.Initialize(panelRoot, mapSize, dayTexture, _earthTransform, _cameraController);
        mapPanel.RootRect.anchorMin = new Vector2(0.5f, 1f);
        mapPanel.RootRect.anchorMax = new Vector2(0.5f, 1f);
        mapPanel.RootRect.pivot = new Vector2(0.5f, 1f);
        mapPanel.RootRect.anchoredPosition = new Vector2(0f, -40f);

        var listPanel = panelRoot.gameObject.AddComponent<SpaceObjectListPanel>();
        Vector2 listSize = new Vector2(536f, 300f);
        listPanel.Initialize(panelRoot, listSize, _cameraController);
        listPanel.ScrollRoot.anchorMin = new Vector2(0.5f, 1f);
        listPanel.ScrollRoot.anchorMax = new Vector2(0.5f, 1f);
        listPanel.ScrollRoot.pivot = new Vector2(0.5f, 1f);
        listPanel.ScrollRoot.anchoredPosition = new Vector2(0f, -40f - mapSize.y - 16f);
    }

    void BuildPlaceholderPanelContent(RectTransform panelRoot, string message)
    {
        TextMeshProUGUI text = CreateText(panelRoot, message, 13f, FontStyles.Normal, PlaceholderTextColor);
        text.alignment = TextAlignmentOptions.Center;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(20f, 20f);
        rect.offsetMax = new Vector2(-20f, -40f);
    }

    void ToggleEntry(DockEntry entry)
    {
        if (entry.IsOpen)
            ClosePanel(entry);
        else
            OpenPanel(entry);
    }

    void OpenPanel(DockEntry entry)
    {
        entry.IsOpen = true;
        entry.ButtonBackground.color = DockButtonHighlightColor;
        entry.PanelRoot.gameObject.SetActive(true);

        if (entry.ActiveAnimation != null)
            StopCoroutine(entry.ActiveAnimation);
        entry.ActiveAnimation = StartCoroutine(AnimatePanel(entry, true));
    }

    void ClosePanel(DockEntry entry)
    {
        entry.IsOpen = false;
        entry.ButtonBackground.color = DockButtonColor;

        if (entry.ActiveAnimation != null)
            StopCoroutine(entry.ActiveAnimation);
        entry.ActiveAnimation = StartCoroutine(AnimatePanel(entry, false));
    }

    IEnumerator AnimatePanel(DockEntry entry, bool opening)
    {
        Vector2 hiddenPosition = entry.OpenAnchoredPosition + new Vector2(_openPopOffsetPixels, 0f);
        Vector2 from = opening ? hiddenPosition : entry.OpenAnchoredPosition;
        Vector2 to = opening ? entry.OpenAnchoredPosition : hiddenPosition;
        float fromAlpha = opening ? 0f : 1f;
        float toAlpha = opening ? 1f : 0f;

        entry.PanelGroup.blocksRaycasts = opening;
        entry.PanelGroup.interactable = opening;

        float elapsedSeconds = 0f;
        while (elapsedSeconds < _openAnimationSeconds)
        {
            elapsedSeconds += Time.unscaledDeltaTime;
            float normalizedTime = Mathf.Clamp01(elapsedSeconds / _openAnimationSeconds);
            float easedTime = normalizedTime * normalizedTime * (3f - 2f * normalizedTime);

            entry.PanelRoot.anchoredPosition = Vector2.Lerp(from, to, easedTime);
            entry.PanelGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, easedTime);
            yield return null;
        }

        entry.PanelRoot.anchoredPosition = to;
        entry.PanelGroup.alpha = toAlpha;

        if (!opening)
            entry.PanelRoot.gameObject.SetActive(false);

        entry.ActiveAnimation = null;
    }

    static TextMeshProUGUI CreateText(Transform parent, string content, float fontSize, FontStyles style, Color color)
    {
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(parent, false);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }
}
