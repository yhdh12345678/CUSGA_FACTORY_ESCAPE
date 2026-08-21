using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class PortraitTextPresentation : MonoBehaviour
{
    public event Action LayoutRebuilt;

    public sealed class Entry
    {
        public string key;
        public string label;
        public string value;
        public bool actionable;
        public bool editable;
        public bool disabled;
        public bool header;
        public Func<bool> activate;
        public Action<string> setValue;
    }

    public static readonly Vector2 ReferenceResolution = new Vector2(1080f, 2400f);

    private readonly Dictionary<string, RectTransform> rows = new Dictionary<string, RectTransform>();
    private readonly List<Selectable> selectables = new List<Selectable>();
    private readonly Vector3[] corners = new Vector3[4];
    private RectTransform safeRoot;
    private RectTransform content;
    private Image artworkBackground;
    private Image artwork;
    private Image titleBackground;
    private Image contentPanel;
    private TMP_Text title;
    private ScrollRect scroll;
    private TMP_FontAsset font;
    private Material presentationFontMaterial;
    private Rect lastSafeArea;
    private Coroutine delayedRebuild;

    public Transform PresentationRoot { get; private set; }
    public int EntryCount => rows.Count;

    private void Awake()
    {
        font = SystemChineseFontProvider.CurrentFont;
        if (font != null && font.material != null)
        {
            presentationFontMaterial = new Material(font.material)
            {
                name = "Portrait Presentation Font Material"
            };
        }
        CreateCanvas();
    }

    private void OnDestroy()
    {
        if (presentationFontMaterial != null)
        {
            Destroy(presentationFontMaterial);
        }
    }

    private void Update()
    {
        if (lastSafeArea != Screen.safeArea)
        {
            ApplySafeArea();
        }
    }

    public bool Contains(Transform candidate)
    {
        return PresentationRoot != null && candidate != null &&
               candidate.IsChildOf(PresentationRoot);
    }

    public void Present(string pageTitle, Sprite currentArtwork, IReadOnlyList<Entry> entries)
    {
        string resolvedTitle = string.IsNullOrWhiteSpace(pageTitle) ? "文字冒险屋" : pageTitle;
        if (title.text != resolvedTitle)
        {
            title.text = resolvedTitle;
        }
        SetArtwork(currentArtwork);

        for (int index = content.childCount - 1; index >= 0; index--)
        {
            Destroy(content.GetChild(index).gameObject);
        }

        rows.Clear();
        selectables.Clear();
        scroll.StopMovement();
        scroll.velocity = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        foreach (Entry entry in entries)
        {
            RectTransform row = entry.editable
                ? CreateInput(entry)
                : entry.actionable
                    ? CreateButton(entry)
                    : CreateStaticText(entry);
            rows[entry.key] = row;
        }

        ConfigureNavigation();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        content.anchoredPosition = Vector2.zero;
        Canvas.ForceUpdateCanvases();
        if (delayedRebuild != null)
        {
            StopCoroutine(delayedRebuild);
        }
        delayedRebuild = StartCoroutine(RebuildAfterDynamicGlyphs());
    }

    private IEnumerator RebuildAfterDynamicGlyphs()
    {
        yield return null;
        title.ForceMeshUpdate();
        foreach (TMP_Text text in content.GetComponentsInChildren<TMP_Text>(false))
        {
            text.ForceMeshUpdate();
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        Canvas.ForceUpdateCanvases();
        delayedRebuild = null;
        LayoutRebuilt?.Invoke();
    }

    public void SetArtwork(Sprite currentArtwork)
    {
        artwork.sprite = currentArtwork;
        artwork.enabled = currentArtwork != null;
        artworkBackground.gameObject.SetActive(currentArtwork != null);
        if (currentArtwork == null)
        {
            SetAnchors(titleBackground.rectTransform, new Vector2(0f, 0.92f), Vector2.one);
            SetAnchors(contentPanel.rectTransform, Vector2.zero, new Vector2(1f, 0.92f));
        }
        else
        {
            SetAnchors(titleBackground.rectTransform, new Vector2(0f, 0.67f), new Vector2(1f, 0.735f));
            SetAnchors(contentPanel.rectTransform, Vector2.zero, new Vector2(1f, 0.67f));
        }
    }

    public bool TryGetScreenFrame(string key, out Rect frame)
    {
        if (!rows.TryGetValue(key, out RectTransform row))
        {
            frame = default;
            return false;
        }

        row.GetWorldCorners(corners);
        float minX = Mathf.Min(corners[0].x, corners[2].x);
        float maxX = Mathf.Max(corners[0].x, corners[2].x);
        float minY = Mathf.Min(corners[0].y, corners[2].y);
        float maxY = Mathf.Max(corners[0].y, corners[2].y);
        frame = new Rect(minX, Screen.height - maxY, maxX - minX, maxY - minY);
        return frame.width > 0f && frame.height > 0f;
    }

    private void CreateCanvas()
    {
        var canvasObject = new GameObject(
            "PortraitPresentation",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        PresentationRoot = canvasObject.transform;

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        Image shield = CreateImage("Background", canvasRect, new Color32(3, 8, 10, 255));
        shield.raycastTarget = true;
        Stretch(shield.rectTransform);

        safeRoot = CreateRect("SafeArea", canvasRect);
        ApplySafeArea();

        artworkBackground = CreateImage("ArtworkBackground", safeRoot, new Color32(3, 8, 10, 255));
        SetAnchors(artworkBackground.rectTransform, new Vector2(0f, 0.67f), Vector2.one);

        artwork = CreateImage("Artwork", artworkBackground.rectTransform, Color.white);
        artwork.preserveAspect = true;
        artwork.raycastTarget = false;
        Stretch(artwork.rectTransform, 24f);

        titleBackground = CreateImage("TitleBackground", safeRoot, new Color32(5, 17, 20, 238));
        SetAnchors(titleBackground.rectTransform, new Vector2(0f, 0.67f), new Vector2(1f, 0.735f));
        title = CreateText("Title", titleBackground.rectTransform, 48f, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        Stretch(title.rectTransform, 20f);

        contentPanel = CreateImage("ContentPanel", safeRoot, new Color32(7, 18, 22, 252));
        SetAnchors(contentPanel.rectTransform, Vector2.zero, new Vector2(1f, 0.67f));

        RectTransform viewport = CreateRect("Viewport", contentPanel.rectTransform);
        Stretch(viewport, 28f);
        viewport.gameObject.AddComponent<RectMask2D>();

        content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = contentPanel.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 64f;
        titleBackground.transform.SetAsLastSibling();
    }

    private RectTransform CreateStaticText(Entry entry)
    {
        Image row = CreateImage("Text_" + entry.key, content, new Color32(12, 31, 37, 245));
        float preferredHeight = entry.header
            ? 126f
            : Mathf.Max(150f, 58f + Mathf.Ceil(DisplayText(entry).Length / 22f) * 54f);
        AddLayout(row.gameObject, preferredHeight);
        TMP_Text text = CreateText("Label", row.rectTransform, entry.header ? 44f : 38f,
            entry.header ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft);
        text.fontStyle = entry.header ? FontStyles.Bold : FontStyles.Normal;
        text.text = DisplayText(entry);
        Stretch(text.rectTransform, 28f);
        return row.rectTransform;
    }

    private RectTransform CreateButton(Entry entry)
    {
        Image row = CreateImage("Button_" + entry.key, content,
            entry.disabled ? new Color32(40, 48, 50, 255) : new Color32(18, 65, 76, 255));
        AddLayout(row.gameObject, 116f);
        Button button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = row;
        button.interactable = !entry.disabled;
        button.onClick.AddListener(() => entry.activate?.Invoke());

        TMP_Text text = CreateText("Label", row.rectTransform, 40f, TextAlignmentOptions.Center);
        text.fontStyle = FontStyles.Bold;
        text.text = DisplayText(entry);
        Stretch(text.rectTransform, 24f);
        selectables.Add(button);
        return row.rectTransform;
    }

    private RectTransform CreateInput(Entry entry)
    {
        Image row = CreateImage("Input_" + entry.key, content, new Color32(14, 42, 49, 255));
        AddLayout(row.gameObject, 128f);
        TMP_InputField input = row.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = row;

        RectTransform viewport = CreateRect("TextViewport", row.rectTransform);
        Stretch(viewport, 28f);
        viewport.gameObject.AddComponent<RectMask2D>();

        TMP_Text text = CreateText("Text", viewport, 38f, TextAlignmentOptions.MidlineLeft);
        Stretch(text.rectTransform);
        TMP_Text placeholder = CreateText("Placeholder", viewport, 38f, TextAlignmentOptions.MidlineLeft);
        placeholder.text = entry.label;
        placeholder.color = new Color32(180, 199, 202, 255);
        Stretch(placeholder.rectTransform);

        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.text = entry.value ?? string.Empty;
        input.onSelect.AddListener(_ => entry.activate?.Invoke());
        input.onValueChanged.AddListener(value => entry.setValue?.Invoke(value));
        selectables.Add(input);
        return row.rectTransform;
    }

    private void ConfigureNavigation()
    {
        for (int index = 0; index < selectables.Count; index++)
        {
            Navigation navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = index > 0 ? selectables[index - 1] : null,
                selectOnDown = index + 1 < selectables.Count ? selectables[index + 1] : null
            };
            selectables[index].navigation = navigation;
        }

        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null &&
            selectables.Count > 0)
        {
            EventSystem.current.SetSelectedGameObject(selectables[0].gameObject);
        }
    }

    private TMP_Text CreateText(string name, Transform parent, float size, TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(name, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSharedMaterial = presentationFontMaterial;
        text.fontSize = size;
        text.color = new Color32(242, 255, 255, 255);
        text.alignment = alignment;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static string DisplayText(Entry entry)
    {
        return string.IsNullOrWhiteSpace(entry.value)
            ? entry.label
            : entry.label + "，" + entry.value;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child.GetComponent<RectTransform>();
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        RectTransform rect = CreateRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static void AddLayout(GameObject target, float preferredHeight)
    {
        LayoutElement layout = target.AddComponent<LayoutElement>();
        layout.minHeight = preferredHeight;
        layout.preferredHeight = preferredHeight;
    }

    private static void Stretch(RectTransform rect, float margin = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(margin, margin);
        rect.offsetMax = new Vector2(-margin, -margin);
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void ApplySafeArea()
    {
        lastSafeArea = Screen.safeArea;
        if (Screen.width <= 0 || Screen.height <= 0)
        {
            return;
        }

        safeRoot.anchorMin = new Vector2(lastSafeArea.xMin / Screen.width, lastSafeArea.yMin / Screen.height);
        safeRoot.anchorMax = new Vector2(lastSafeArea.xMax / Screen.width, lastSafeArea.yMax / Screen.height);
        safeRoot.offsetMin = Vector2.zero;
        safeRoot.offsetMax = Vector2.zero;
    }
}
