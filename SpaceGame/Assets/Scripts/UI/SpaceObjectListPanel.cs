using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds and maintains the categorized craft/satellite list inside a given parent
/// RectTransform: one section per SpaceObjectCategory, each listing every currently tracked
/// SatelliteFlareController in that category. Clicking a row flies the camera to that craft,
/// same as clicking its glare in the 3D view.
/// </summary>
public class SpaceObjectListPanel : MonoBehaviour
{
    const float ListRefreshIntervalSeconds = 1f;
    const float RowHeightPixels = 26f;
    const float SectionHeaderHeightPixels = 20f;
    const float SectionSpacingPixels = 10f;
    const float CategoryDotSizePixels = 10f;

    static readonly SpaceObjectCategory[] Categories =
    {
        SpaceObjectCategory.InformationSatellite,
        SpaceObjectCategory.UnmannedCraft,
        SpaceObjectCategory.MannedCraft,
        SpaceObjectCategory.Weapon
    };

    static readonly Color EmptyRowTextColor = new Color(0.6f, 0.6f, 0.65f, 0.7f);
    static readonly Color RowBackgroundColor = new Color(1f, 1f, 1f, 0.04f);

    CameraController _cameraController;
    RectTransform _content;
    float _refreshAccumulatorSeconds;

    /// <summary>The scrollable list's own RectTransform; callers position it within their layout.</summary>
    public RectTransform ScrollRoot { get; private set; }

    /// <summary>Builds the scrollable, categorized craft list as a child of the given parent.</summary>
    public void Initialize(Transform parent, Vector2 size, CameraController cameraController)
    {
        _cameraController = cameraController;

        var scrollGo = new GameObject("CraftListScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(parent, false);
        ScrollRoot = scrollGo.GetComponent<RectTransform>();
        ScrollRoot.sizeDelta = size;

        var scrollBackground = scrollGo.AddComponent<Image>();
        scrollBackground.color = new Color(0f, 0f, 0f, 0.25f);

        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        var viewportRect = viewportGo.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewportGo.AddComponent<RectMask2D>();
        scrollRect.viewport = viewportRect;

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        _content = contentGo.GetComponent<RectTransform>();
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero;

        var layoutGroup = contentGo.AddComponent<VerticalLayoutGroup>();
        layoutGroup.padding = new RectOffset(8, 8, 8, 8);
        layoutGroup.spacing = SectionSpacingPixels;
        layoutGroup.childControlWidth = true;
        layoutGroup.childControlHeight = true;
        layoutGroup.childForceExpandWidth = true;
        layoutGroup.childForceExpandHeight = false;

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content = _content;

        RebuildList();
    }

    void Update()
    {
        if (_content == null)
            return;

        _refreshAccumulatorSeconds += Time.unscaledDeltaTime;
        if (_refreshAccumulatorSeconds < ListRefreshIntervalSeconds)
            return;
        _refreshAccumulatorSeconds = 0f;

        RebuildList();
    }

    void RebuildList()
    {
        for (int i = _content.childCount - 1; i >= 0; i--)
            Destroy(_content.GetChild(i).gameObject);

        IReadOnlyList<SatelliteFlareController> craftList = SatelliteFlareController.All;

        for (int categoryIndex = 0; categoryIndex < Categories.Length; categoryIndex++)
            BuildSection(Categories[categoryIndex], craftList);
    }

    void BuildSection(SpaceObjectCategory category, IReadOnlyList<SatelliteFlareController> craftList)
    {
        var sectionGo = new GameObject($"Section_{category}", typeof(RectTransform));
        sectionGo.transform.SetParent(_content, false);
        var sectionLayout = sectionGo.AddComponent<VerticalLayoutGroup>();
        sectionLayout.spacing = 2f;
        sectionLayout.childControlWidth = true;
        sectionLayout.childControlHeight = true;
        sectionLayout.childForceExpandWidth = true;
        sectionLayout.childForceExpandHeight = false;
        sectionGo.AddComponent<LayoutElement>();

        TextMeshProUGUI header = CreateText(
            sectionGo.transform, SpaceObjectCategoryStyle.GetHeaderLabel(category),
            12f, FontStyles.Bold, SpaceObjectCategoryStyle.GetColor(category));
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = SectionHeaderHeightPixels;

        bool hasEntry = false;
        for (int i = 0; i < craftList.Count; i++)
        {
            SatelliteFlareController craft = craftList[i];
            if (craft == null || craft.Category != category)
                continue;
            hasEntry = true;
            BuildRow(sectionGo.transform, craft);
        }

        if (!hasEntry)
        {
            TextMeshProUGUI emptyText = CreateText(sectionGo.transform, "-- none tracked --", 10f, FontStyles.Italic, EmptyRowTextColor);
            emptyText.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeightPixels;
        }
    }

    void BuildRow(Transform parent, SatelliteFlareController craft)
    {
        var rowGo = new GameObject($"Row_{craft.DisplayName}", typeof(RectTransform));
        rowGo.transform.SetParent(parent, false);
        rowGo.AddComponent<LayoutElement>().preferredHeight = RowHeightPixels;

        var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 6f;
        rowLayout.padding = new RectOffset(4, 4, 2, 2);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        var background = rowGo.AddComponent<Image>();
        background.color = RowBackgroundColor;

        var button = rowGo.AddComponent<Button>();
        button.targetGraphic = background;

        Transform craftTransform = craft.transform;
        CameraController cameraController = _cameraController;
        button.onClick.AddListener(() =>
        {
            if (cameraController != null)
                cameraController.FocusOnTarget(craftTransform);
        });

        var dotGo = new GameObject("CategoryDot", typeof(RectTransform));
        dotGo.transform.SetParent(rowGo.transform, false);
        var dotLayout = dotGo.AddComponent<LayoutElement>();
        dotLayout.preferredWidth = CategoryDotSizePixels;
        dotLayout.preferredHeight = CategoryDotSizePixels;
        var dotImage = dotGo.AddComponent<Image>();
        dotImage.color = SpaceObjectCategoryStyle.GetColor(craft.Category);

        TextMeshProUGUI label = CreateText(rowGo.transform, craft.DisplayName, 12f, FontStyles.Normal, Color.white);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        label.raycastTarget = false;
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
