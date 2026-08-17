using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

public class ContactStageHud : MonoBehaviour
{
    [Header("References")]
    [SerializeField] ContactStageManager _stageManager;

    [Header("Layout")]
    [SerializeField] Vector2 _screenMargin = new Vector2(24f, 24f);
    [SerializeField] float _panelWidth = 520f;

    static readonly ContactStage[] s_stages =
    {
        ContactStage.Undetected,
        ContactStage.PassiveDetect,
        ContactStage.NonAggressive,
        ContactStage.ReactiveUnmanned,
        ContactStage.ReactiveCrewed,
        ContactStage.CounterAttack,
        ContactStage.Indiscriminate,
        ContactStage.Collapse
    };

    static readonly string[] s_stageLabels =
    {
        "UNDETECTED",
        "PASSIVE",
        "NON-AGGR.",
        "REACTIVE / U",
        "REACTIVE / C",
        "COUNTER",
        "INDISCRIM.",
        "COLLAPSE"
    };

    static readonly Color s_panelColor = new Color(0.018f, 0.028f, 0.045f, 0.94f);
    static readonly Color s_inactiveColor = new Color(0.12f, 0.17f, 0.22f, 0.92f);
    static readonly Color s_futureTextColor = new Color(0.68f, 0.76f, 0.82f, 0.7f);
    static readonly Color s_activeColor = new Color(0.16f, 0.72f, 0.78f, 1f);
    static readonly Color s_dangerColor = new Color(0.82f, 0.25f, 0.22f, 1f);

    Canvas _canvas;
    RectTransform _panelRect;
    TextMeshProUGUI _statusText;
    TextMeshProUGUI _toggleLabel;
    RectTransform _stageRowRect;
    TextMeshProUGUI[] _stageTexts;
    TextMeshProUGUI[] _arrowTexts;
    Image[] _stageBackgrounds;
    Button _toggleButton;
    ContactStage _currentStage;
    WorldSimulation _simulation;
    int _direction = 1;
    bool _expanded = true;

    void Awake()
    {
        BuildUi();
    }

    void Start()
    {
        _simulation = GetComponent<WorldSimulation>();

        if (_stageManager == null)
            _stageManager = ContactStageManager.Instance;

        if (_stageManager != null)
        {
            _currentStage = _stageManager.CurrentStage;
            _stageManager.OnStageChanged += HandleStageChanged;
        }

        RefreshUi();
    }

    void Update()
    {
        if (_statusText != null && _simulation != null)
            _statusText.text = BuildStatusText();

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && _toggleButton != null)
        {
            Vector2 screenPosition = Mouse.current.position.ReadValue();
            if (RectTransformUtility.RectangleContainsScreenPoint(_toggleButton.transform as RectTransform, screenPosition, null))
                ToggleExpanded();
        }
    }

    void OnDestroy()
    {
        if (_stageManager != null)
            _stageManager.OnStageChanged -= HandleStageChanged;
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("ContactStageCanvas");
        canvasGo.transform.SetParent(transform, false);

        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 20;
        canvasGo.AddComponent<GraphicRaycaster>();

        var canvasScaler = canvasGo.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasScaler.matchWidthOrHeight = 0.5f;

        var panelGo = new GameObject("ContactStagePanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        _panelRect = panelGo.AddComponent<RectTransform>();
        _panelRect.anchorMin = new Vector2(0f, 1f);
        _panelRect.anchorMax = new Vector2(0f, 1f);
        _panelRect.pivot = new Vector2(0f, 1f);
        _panelRect.anchoredPosition = new Vector2(_screenMargin.x, -_screenMargin.y);
        _panelRect.sizeDelta = new Vector2(_panelWidth, 76f);

        var panelBackground = panelGo.AddComponent<Image>();
        panelBackground.color = s_panelColor;

        var panelLayout = panelGo.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(10, 10, 8, 8);
        panelLayout.spacing = 3f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;
        panelLayout.enabled = false;

        var headerGo = new GameObject("Header", typeof(RectTransform));
        headerGo.transform.SetParent(panelGo.transform, false);
        var headerRect = headerGo.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = new Vector2(0f, -8f);
        headerRect.sizeDelta = new Vector2(-20f, 16f);
        var headerLayout = headerGo.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 6f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = true;
        var headerElement = headerGo.AddComponent<LayoutElement>();
        headerElement.preferredHeight = 16f;

        _statusText = CreateText(headerGo.transform, 10f, FontStyles.Bold, new Color(0.68f, 0.82f, 0.86f, 0.9f));
        var statusElement = _statusText.gameObject.AddComponent<LayoutElement>();
        statusElement.flexibleWidth = 1f;

        var buttonGo = new GameObject("ExpandCollapseButton");
        buttonGo.transform.SetParent(headerGo.transform, false);
        var buttonElement = buttonGo.AddComponent<LayoutElement>();
        buttonElement.preferredWidth = 64f;
        buttonElement.preferredHeight = 16f;

        var buttonImage = buttonGo.AddComponent<Image>();
        buttonImage.color = s_inactiveColor;
        _toggleButton = buttonGo.AddComponent<Button>();
        _toggleButton.targetGraphic = buttonImage;

        _toggleLabel = CreateText(buttonGo.transform, 8f, FontStyles.Bold, s_futureTextColor);
        _toggleLabel.alignment = TextAlignmentOptions.Center;
        var toggleRect = _toggleLabel.rectTransform;
        toggleRect.anchorMin = Vector2.zero;
        toggleRect.anchorMax = Vector2.one;
        toggleRect.offsetMin = Vector2.zero;
        toggleRect.offsetMax = Vector2.zero;

        var stageRowGo = new GameObject("StageRow", typeof(RectTransform));
        stageRowGo.transform.SetParent(panelGo.transform, false);
        _stageRowRect = stageRowGo.GetComponent<RectTransform>();
        _stageRowRect.anchorMin = new Vector2(0f, 1f);
        _stageRowRect.anchorMax = new Vector2(1f, 1f);
        _stageRowRect.pivot = new Vector2(0.5f, 1f);
        _stageRowRect.anchoredPosition = new Vector2(0f, -28f);
        _stageRowRect.sizeDelta = new Vector2(-20f, 32f);
        var rowLayout = stageRowGo.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 2f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;
        var rowElement = stageRowGo.AddComponent<LayoutElement>();
        rowElement.preferredHeight = 32f;

        _stageTexts = new TextMeshProUGUI[s_stages.Length];
        _arrowTexts = new TextMeshProUGUI[s_stages.Length - 1];
        _stageBackgrounds = new Image[s_stages.Length];

        for (int i = 0; i < s_stages.Length; i++)
        {
            var stageGo = new GameObject($"Stage_{i + 1}");
            stageGo.transform.SetParent(_stageRowRect, false);
            var stageBackground = stageGo.AddComponent<Image>();
            stageBackground.color = s_inactiveColor;
            _stageBackgrounds[i] = stageBackground;

            var stageElement = stageGo.AddComponent<LayoutElement>();
            stageElement.preferredWidth = 104f;
            stageElement.preferredHeight = 30f;

            var stageText = CreateText(stageGo.transform, 9f, FontStyles.Bold, s_futureTextColor);
            stageText.alignment = TextAlignmentOptions.Center;
            stageText.text = s_stageLabels[i];
            var textRect = stageText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(3f, 0f);
            textRect.offsetMax = new Vector2(-3f, 0f);
            _stageTexts[i] = stageText;

            if (i >= s_stages.Length - 1) continue;

            var arrowGo = new GameObject($"StageArrow_{i + 1}");
            arrowGo.transform.SetParent(_stageRowRect, false);
            var arrowElement = arrowGo.AddComponent<LayoutElement>();
            arrowElement.preferredWidth = 18f;
            arrowElement.preferredHeight = 30f;

            var arrowText = CreateText(arrowGo.transform, 12f, FontStyles.Bold, s_futureTextColor);
            arrowText.alignment = TextAlignmentOptions.Center;
            arrowText.text = ">>";
            var arrowRect = arrowText.rectTransform;
            arrowRect.anchorMin = Vector2.zero;
            arrowRect.anchorMax = Vector2.one;
            arrowRect.offsetMin = Vector2.zero;
            arrowRect.offsetMax = Vector2.zero;
            _arrowTexts[i] = arrowText;
        }
    }

    TextMeshProUGUI CreateText(Transform parent, float fontSize, FontStyles style, Color color)
    {
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(parent, false);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    void ToggleExpanded()
    {
        _expanded = !_expanded;
        RefreshUi();
    }

    void HandleStageChanged(ContactStage oldStage, ContactStage newStage)
    {
        _direction = (int)newStage >= (int)oldStage ? 1 : -1;
        _currentStage = newStage;
        RefreshUi();
    }

    string BuildStatusText()
    {
        string directionLabel = _direction < 0 ? "DE-ESCALATING  <<" : "ESCALATING  >>";
        if (_simulation == null)
            return $"HUMANITY / CONTACT     {directionLabel}";

        return $"HUMANITY / CONTACT     {directionLabel}     YEAR {_simulation.SimulatedYear:F1}     WARP {_simulation.TimeWarpFactor:0.#}x";
    }

    void RefreshUi()
    {
        if (_stageTexts == null) return;

        int currentIndex = Mathf.Clamp((int)_currentStage - 1, 0, s_stages.Length - 1);
        bool isCollapse = _currentStage == ContactStage.Collapse;
        _statusText.text = BuildStatusText();
        _statusText.color = isCollapse ? s_dangerColor : new Color(0.68f, 0.82f, 0.86f, 0.9f);
        _toggleLabel.text = _expanded ? "MINIMIZE" : "EXPAND";

        for (int i = 0; i < s_stages.Length; i++)
        {
            bool isCurrent = i == currentIndex;
            bool isVisible = _expanded ? Mathf.Abs(i - currentIndex) <= 1 : isCurrent;
            bool isPast = i < currentIndex;
            _stageTexts[i].transform.parent.gameObject.SetActive(isVisible);

            if (!isVisible) continue;

            Color backgroundColor = isCurrent ? (isCollapse ? s_dangerColor : s_activeColor) : s_inactiveColor;
            Color textColor = isCurrent ? Color.white : isPast ? new Color(0.82f, 0.9f, 0.92f, 0.88f) : s_futureTextColor;
            _stageBackgrounds[i].color = backgroundColor;
            _stageTexts[i].color = textColor;
        }

        for (int i = 0; i < _arrowTexts.Length; i++)
        {
            bool isVisible = _expanded && (i == currentIndex - 1 || i == currentIndex);
            _arrowTexts[i].transform.parent.gameObject.SetActive(isVisible);
            if (!isVisible) continue;

            _arrowTexts[i].text = _direction < 0 ? "<<" : ">>";
            _arrowTexts[i].color = isCollapse ? s_dangerColor : s_activeColor;
        }

        _panelRect.sizeDelta = new Vector2(_panelWidth, _expanded ? 76f : 48f);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_stageRowRect);
    }
}
