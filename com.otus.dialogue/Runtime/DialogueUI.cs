using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 运行时构建的对话 UI（纸质风格 + 左侧角色头像）。由 DialogueManager 持有的普通类，非 MonoBehaviour。
/// 层级：DialogueCanvas > Panel(纸面) > PortraitFrame(头像框) > PortraitImage/PortraitInitial
///                                    > NamePlate(姓名牌) > NameText / BodyText / ContinueArrow
///                  > ChoiceRoot(玩家选项容器，按需激活) + DialogueEventSystem(Button 点击依赖)。
/// Build 只建结构，样式赋值统一走 ApplyStyle（单点，杜绝两处漂移）。
/// </summary>
public class DialogueUI
{
    // 布局常量（视觉验证后可微调）
    private const float PortraitSize = 104f;
    private const float PortraitMarginLeft = 20f;
    private const float PortraitInset = 18f;   // 头像内缩（略小于 border 22，纸框花纹不全吃掉）
    private const float BodyDefaultX = 28f;    // 无头像时正文左边距
    private const float BodyIndentX = 140f;    // 有头像时正文左边距

    private GameObject _panel;
    private GameObject _choiceRoot;
    private GameObject _namePlate;
    private TextMeshProUGUI _name;
    private TextMeshProUGUI _body;
    private TextMeshProUGUI _arrow;

    // 历史窗（HistoryRoot 全屏黑底 > Title / Scroll > Viewport > Content / Hint）
    private GameObject _historyRoot;
    private RectTransform _historyContent;
    private ScrollRect _historyScroll;
    private TextMeshProUGUI _historyTitle;
    private TextMeshProUGUI _historyHint;
    private readonly List<TextMeshProUGUI> _historyEntries = new List<TextMeshProUGUI>(); // ApplyStyle 就地重刷
    private TextMeshProUGUI _autoBadge;

    // 交互提示（InteractPrompt：纸面小框 + 文本，挂 Canvas 与 Panel 平级——播放态显示 Panel、非播放态显示它）
    private GameObject _interactPromptRoot;
    private Image _interactPromptImage;
    private TextMeshProUGUI _interactPrompt;

    // ApplyStyle 就地更新目标
    private Image _panelImage;
    private Image _plateImage;
    private GameObject _portraitRoot;
    private Image _portraitFrameImage;
    private Image _portraitImage;
    private TextMeshProUGUI _portraitInitial;
    private DialogueUIConfig _activeConfig;

    /// <summary>正文 TMP，Manager 直接操作 maxVisibleCharacters 驱动打字机。</summary>
    public TextMeshProUGUI Body => _body;

    public void Build(Transform parent, DialogueUIConfig cfg)
    {
        if (_panel != null)
        {
            return; // 已构建（为将来迁预制体留路）
        }

        // ---- Canvas ----
        var canvasGo = new GameObject("DialogueCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(parent, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;

        // ---- Panel：纸质对话框，贴底占 25% 高 ----
        _panel = new GameObject("Panel", typeof(Image));
        _panel.transform.SetParent(canvasGo.transform, false);
        _panelImage = _panel.GetComponent<Image>();
        var panelRect = _panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0);
        panelRect.anchorMax = new Vector2(1, 0.25f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // ---- PortraitFrame：左侧角色头像框（Frame > Image/首字缩写）----
        _portraitRoot = new GameObject("PortraitFrame", typeof(Image));
        _portraitRoot.transform.SetParent(_panel.transform, false);
        _portraitFrameImage = _portraitRoot.GetComponent<Image>();
        var frameRect = _portraitRoot.GetComponent<RectTransform>();
        frameRect.anchorMin = new Vector2(0, 0.5f);
        frameRect.anchorMax = new Vector2(0, 0.5f);
        frameRect.pivot = new Vector2(0, 0.5f);
        frameRect.anchoredPosition = new Vector2(PortraitMarginLeft, 0);
        frameRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);

        _portraitImage = CreateImage("PortraitImage", _portraitRoot.transform);
        var portraitRect = _portraitImage.rectTransform;
        portraitRect.anchorMin = Vector2.zero;
        portraitRect.anchorMax = Vector2.one;
        portraitRect.offsetMin = new Vector2(PortraitInset, PortraitInset);
        portraitRect.offsetMax = new Vector2(-PortraitInset, -PortraitInset);
        _portraitImage.preserveAspect = true;

        _portraitInitial = CreateText("PortraitInitial", _portraitRoot.transform, null);
        var initialRect = _portraitInitial.rectTransform;
        initialRect.anchorMin = Vector2.zero;
        initialRect.anchorMax = Vector2.one;
        initialRect.offsetMin = new Vector2(PortraitInset, PortraitInset);
        initialRect.offsetMax = new Vector2(-PortraitInset, -PortraitInset);
        _portraitInitial.fontSize = 44;
        _portraitInitial.alignment = TextAlignmentOptions.Center;

        // ---- NamePlate：姓名牌，悬浮于面板左上 ----
        _namePlate = new GameObject("NamePlate", typeof(Image));
        _namePlate.transform.SetParent(_panel.transform, false);
        _plateImage = _namePlate.GetComponent<Image>();
        var plateRect = _namePlate.GetComponent<RectTransform>();
        plateRect.anchorMin = new Vector2(0, 1);
        plateRect.anchorMax = new Vector2(0, 1);
        plateRect.pivot = new Vector2(0, 0); // 左下角为轴：向上悬浮，底部压进面板
        plateRect.anchoredPosition = new Vector2(24, -30);
        plateRect.sizeDelta = new Vector2(240, 56);

        // ---- NameText ----
        _name = CreateText("NameText", _namePlate.transform, null);
        var nameRect = _name.rectTransform;
        nameRect.anchorMin = Vector2.zero;
        nameRect.anchorMax = Vector2.one;
        nameRect.offsetMin = new Vector2(16, 2);
        nameRect.offsetMax = new Vector2(-16, -2);
        _name.fontSize = 28;
        _name.alignment = TextAlignmentOptions.Center;

        // ---- BodyText：正文，四边拉伸留 padding ----
        _body = CreateText("BodyText", _panel.transform, null);
        var bodyRect = _body.rectTransform;
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(BodyDefaultX, 14);
        bodyRect.offsetMax = new Vector2(-28, -20);
        _body.fontSize = 30;
        _body.enableWordWrapping = true;
        _body.lineSpacing = 8f; // 额外行距，缓解默认行高偏紧

        // ---- ContinueArrow：右下角"▼" ----
        _arrow = CreateText("ContinueArrow", _panel.transform, null);
        _arrow.text = "▼";
        _arrow.fontSize = 28;
        var arrowRect = _arrow.rectTransform;
        arrowRect.anchorMin = new Vector2(1, 0);
        arrowRect.anchorMax = new Vector2(1, 0);
        arrowRect.pivot = new Vector2(1, 0);
        arrowRect.anchoredPosition = new Vector2(-16, 26);

        // ---- ChoiceRoot：玩家选项容器，悬浮于面板上方；ShowChoices 时才激活 ----
        _choiceRoot = new GameObject("ChoiceRoot", typeof(RectTransform));
        _choiceRoot.transform.SetParent(canvasGo.transform, false);
        var choiceRect = (RectTransform)_choiceRoot.transform;
        choiceRect.anchorMin = new Vector2(0.5f, 1f);
        choiceRect.anchorMax = new Vector2(0.5f, 1f);
        choiceRect.pivot = new Vector2(0.5f, 1f);
        choiceRect.anchoredPosition = new Vector2(0, -36f);
        var choiceLayout = _choiceRoot.AddComponent<VerticalLayoutGroup>();
        choiceLayout.spacing = 8f;
        choiceLayout.childForceExpandWidth = false;
        choiceLayout.childForceExpandHeight = false;
        choiceLayout.childControlWidth = true;
        choiceLayout.childControlHeight = true;
        var choiceFitter = _choiceRoot.AddComponent<ContentSizeFitter>();
        choiceFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _choiceRoot.SetActive(false);

        // ---- HistoryRoot：全屏历史窗（黑底挡射线），ShowHistory 时才激活 ----
        _historyRoot = new GameObject("HistoryRoot", typeof(Image));
        _historyRoot.transform.SetParent(canvasGo.transform, false);
        var historyBg = _historyRoot.GetComponent<Image>();
        historyBg.color = new Color(0f, 0f, 0f, 0.85f);
        historyBg.raycastTarget = true; // 挡住底下对话面板的射线
        var historyRect = (RectTransform)_historyRoot.transform;
        historyRect.anchorMin = Vector2.zero;
        historyRect.anchorMax = Vector2.one;
        historyRect.offsetMin = Vector2.zero;
        historyRect.offsetMax = Vector2.zero;
        _historyRoot.SetActive(false);

        _historyTitle = CreateText("Title", _historyRoot.transform, null);
        _historyTitle.text = "对话历史";
        _historyTitle.fontSize = 34;
        _historyTitle.alignment = TextAlignmentOptions.Center;
        var titleRect = _historyTitle.rectTransform;
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(0, 60f);

        // ScrollView：viewport 用 RectMask2D 裁剪，content 垂直布局 + 高度自适应
        var scrollGo = new GameObject("HistoryScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(_historyRoot.transform, false);
        var scrollBg = scrollGo.GetComponent<Image>();
        scrollBg.color = Color.clear; // 仅作 ScrollRect 射线落点，纯透明
        var scrollRectT = (RectTransform)scrollGo.transform;
        scrollRectT.anchorMin = Vector2.zero;
        scrollRectT.anchorMax = Vector2.one;
        scrollRectT.offsetMin = new Vector2(60f, 70f);
        scrollRectT.offsetMax = new Vector2(-60f, -70f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRect = (RectTransform)viewport.transform;
        vpRect.anchorMin = Vector2.zero;
        vpRect.anchorMax = Vector2.one;
        vpRect.offsetMin = Vector2.zero;
        vpRect.offsetMax = Vector2.zero;

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewport.transform, false);
        _historyContent = (RectTransform)contentGo.transform;
        _historyContent.anchorMin = new Vector2(0, 1);
        _historyContent.anchorMax = new Vector2(1, 1);
        _historyContent.pivot = new Vector2(0.5f, 1f);
        _historyContent.sizeDelta = new Vector2(0, 0);

        var contentLayout = contentGo.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 10f;
        contentLayout.childForceExpandWidth = false;
        contentLayout.childForceExpandHeight = false;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;

        var contentFitter = contentGo.GetComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _historyScroll = scrollGo.GetComponent<ScrollRect>();
        _historyScroll.viewport = vpRect;
        _historyScroll.content = _historyContent;
        _historyScroll.horizontal = false;
        _historyScroll.movementType = ScrollRect.MovementType.Clamped;
        _historyScroll.scrollSensitivity = 30f;

        _historyHint = CreateText("Hint", _historyRoot.transform, null);
        _historyHint.text = "按 H 关闭";
        _historyHint.fontSize = 20;
        _historyHint.alignment = TextAlignmentOptions.Center;
        var hintRect = _historyHint.rectTransform;
        hintRect.anchorMin = new Vector2(0, 0);
        hintRect.anchorMax = new Vector2(1, 0);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.sizeDelta = new Vector2(0, 40f);

        // ---- AutoBadge：AUTO 播放角标，悬浮于面板右上（与姓名牌对称）----
        _autoBadge = CreateText("AutoBadge", _panel.transform, null);
        _autoBadge.text = "AUTO";
        _autoBadge.fontSize = 18;
        _autoBadge.alignment = TextAlignmentOptions.Center;
        var badgeRect = _autoBadge.rectTransform;
        badgeRect.anchorMin = new Vector2(1, 1);
        badgeRect.anchorMax = new Vector2(1, 1);
        badgeRect.pivot = new Vector2(1, 0);
        badgeRect.anchoredPosition = new Vector2(-24, -8);
        badgeRect.sizeDelta = new Vector2(70, 30);
        _autoBadge.gameObject.SetActive(false);

        // ---- InteractPrompt：靠近提示「按 E 交谈」，悬浮于面板正上方（面板占底部 25% = 180px）----
        _interactPromptRoot = new GameObject("InteractPrompt", typeof(Image));
        _interactPromptRoot.transform.SetParent(canvasGo.transform, false);
        _interactPromptImage = _interactPromptRoot.GetComponent<Image>();
        var promptRect = _interactPromptRoot.GetComponent<RectTransform>();
        promptRect.anchorMin = new Vector2(0.5f, 0f);
        promptRect.anchorMax = new Vector2(0.5f, 0f);
        promptRect.pivot = new Vector2(0.5f, 0f);
        promptRect.anchoredPosition = new Vector2(0f, 196f);
        promptRect.sizeDelta = new Vector2(240f, 46f);

        _interactPrompt = CreateText("PromptText", _interactPromptRoot.transform, null);
        _interactPrompt.fontSize = 22;
        _interactPrompt.alignment = TextAlignmentOptions.Center;
        var promptTextRect = _interactPrompt.rectTransform;
        promptTextRect.anchorMin = Vector2.zero;
        promptTextRect.anchorMax = Vector2.one;
        promptTextRect.offsetMin = Vector2.zero;
        promptTextRect.offsetMax = Vector2.zero;
        _interactPromptRoot.SetActive(false);

        // ---- EventSystem：项目场景零摆放且无处保证有 EventSystem，Button 点击依赖它，这里自举 ----
        var eventSystemGo = new GameObject("DialogueEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        eventSystemGo.transform.SetParent(canvasGo.transform, false);

        _panel.SetActive(false); // 默认隐藏（只隐藏 Panel；Manager 常驻，规避自激活陷阱）

        ApplyStyle(cfg); // 样式赋值单点（含 cfg 为 null 的兜底）
    }

    public void Show()
    {
        _panel.SetActive(true);
    }

    public void Hide()
    {
        _panel.SetActive(false);
    }

    /// <summary>
    /// 设置说话人：speaker 资产优先（头像 + 名字颜色覆盖），否则回退旧纯文本名；两者皆空 = 旁白。
    /// </summary>
    public void SetSpeaker(SpeakerAsset speaker, string legacyName)
    {
        string display = speaker != null ? speaker.GetDisplayName() : legacyName;
        bool has = !string.IsNullOrEmpty(display);
        _namePlate.SetActive(has);
        if (has)
        {
            _name.text = display;
        }

        // 头像区：无 speaker（旁白/旧文本）时整体隐藏
        _portraitRoot.SetActive(speaker != null);
        if (speaker != null)
        {
            bool hasPortrait = speaker.Portrait != null;
            _portraitImage.gameObject.SetActive(hasPortrait);
            _portraitInitial.gameObject.SetActive(!hasPortrait);
            if (hasPortrait)
            {
                _portraitImage.sprite = speaker.Portrait;
            }
            else if (display.Length > 0)
            {
                // 取首字；emoji 代理对会截半个字符——中英文名均为单 char，接受
                _portraitInitial.text = display.Substring(0, 1);
            }
        }

        // 名字颜色：speaker 覆盖 > 样式默认
        Color fallback = _activeConfig != null ? _activeConfig.SpeakerColor : Color.white;
        _name.color = speaker != null ? speaker.ResolveNameColor(fallback) : fallback;

        // 正文让位：有头像时右移
        _body.rectTransform.offsetMin = new Vector2(speaker != null ? BodyIndentX : BodyDefaultX, 14f);
    }

    /// <summary>设正文全文并立即重建网格；可见字数清零，由 Manager 逐字递增。</summary>
    public void SetBody(string text)
    {
        _body.text = text;
        _body.maxVisibleCharacters = 0;
        // ignoreActiveState:true —— Panel 同帧刚激活时普通 ForceMeshUpdate 可能不重建，
        // 导致 characterCount 误为 0（打字机瞬间"完成"），必须强制重建
        _body.ForceMeshUpdate(true);
    }

    /// <summary>
    /// 就地重贴皮：贴图/字体/颜色按 cfg 更新（Build 尾部与每次 StartDialogue 换样式时调用，幂等）。
    /// </summary>
    public void ApplyStyle(DialogueUIConfig cfg)
    {
        _activeConfig = cfg;
        cfg ??= NullConfig; // null → 用中性兜底配置统一赋值路径

        ApplySprite(_panelImage, cfg.PanelSprite, new Color(0, 0, 0, 0.75f));
        ApplySprite(_plateImage, cfg.NameSprite, new Color(0.15f, 0.1f, 0.06f, 0.9f));
        ApplySprite(_portraitFrameImage, cfg.PortraitFrameSprite, new Color(0.2f, 0.16f, 0.1f, 0.9f));

        if (cfg.Font != null)
        {
            _name.font = cfg.Font;
            _body.font = cfg.Font;
            _arrow.font = cfg.Font;
            _portraitInitial.font = cfg.Font;
            _historyTitle.font = cfg.Font;
            _historyHint.font = cfg.Font;
            _autoBadge.font = cfg.Font;
        }

        _name.color = cfg.SpeakerColor;
        _body.color = cfg.TextColor;
        _arrow.color = cfg.TextColor;
        _portraitInitial.color = cfg.TextColor;

        // 历史窗与角标跟随换肤（黑底上若 cfg.TextColor 偏暗则由配置方自行调亮，此处不做二次加工）
        Color historyFg = cfg.TextColor;
        _historyTitle.color = historyFg;
        _historyHint.color = historyFg;
        _autoBadge.color = cfg.SpeakerColor;

        // 交互提示跟随换肤（与面板同款纸面/字体/文字色）
        ApplySprite(_interactPromptImage, cfg.PanelSprite, new Color(0, 0, 0, 0.75f));
        if (cfg.Font != null)
        {
            _interactPrompt.font = cfg.Font;
        }

        _interactPrompt.color = cfg.TextColor;
        foreach (var entry in _historyEntries)
        {
            if (cfg.Font != null)
            {
                entry.font = cfg.Font;
            }

            entry.color = historyFg;
        }

        // 换字体后旧网格可能残留（面板 inactive 时普通刷新不重建），强制重建双保险
        _body.ForceMeshUpdate(true);
    }

    public void SetContinueVisible(bool visible)
    {
        _arrow.gameObject.SetActive(visible);
    }

    /// <summary>
    /// 展示玩家选项按钮；点击后经 onSelected 回传索引，跳转与收尾由 Manager 负责。
    /// </summary>
    public void ShowChoices(IReadOnlyList<DialogueChoice> choices, Action<int> onSelected)
    {
        var allEnabled = new bool[choices.Count]; // 默认 false，需显式置 true
        for (int i = 0; i < allEnabled.Length; i++)
        {
            allEnabled[i] = true;
        }

        ShowChoices(choices, onSelected, allEnabled);
    }

    /// <summary>
    /// 带可用标记的选项展示：enabledFlags[i]=false 的项置灰（alpha×0.45）且点击不回调。
    /// </summary>
    public void ShowChoices(IReadOnlyList<DialogueChoice> choices, Action<int> onSelected, IReadOnlyList<bool> enabledFlags)
    {
        ClearChildren(_choiceRoot.transform);

        Color bg = _activeConfig != null ? _activeConfig.ChoiceColor : new Color(0.96f, 0.91f, 0.82f, 0.95f);
        Color fg = _activeConfig != null ? _activeConfig.ChoiceTextColor : new Color(0.35f, 0.22f, 0.12f, 1f);

        for (int i = 0; i < choices.Count; i++)
        {
            // 缺项/null 一律视为可用，避免标记列表长度不一致时误伤
            bool enabled = enabledFlags != null && i < enabledFlags.Count ? enabledFlags[i] : true;

            var go = new GameObject($"Choice_{i}", typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_choiceRoot.transform, false);

            var image = go.GetComponent<Image>();
            image.color = enabled ? bg : Disabled(bg);
            image.raycastTarget = true; // 底图不参与射线检测则按钮永远点不中

            var element = go.GetComponent<LayoutElement>();
            element.minHeight = 40f;
            element.preferredWidth = 360f;

            if (enabled) // 置灰项干脆不挂回调：点击天然无效
            {
                int index = i; // for 循环变量被所有闭包共享，必须拷贝局部副本
                go.GetComponent<Button>().onClick.AddListener(() => onSelected?.Invoke(index));
            }

            var label = CreateText("Label", go.transform, _activeConfig?.Font);
            label.text = choices[i].Text;
            label.fontSize = 24;
            label.alignment = TextAlignmentOptions.Center;
            label.color = enabled ? fg : Disabled(fg);
            label.raycastTarget = true; // 与底图一起保证按钮整体可点
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        _choiceRoot.SetActive(true);
    }

    /// <summary>置灰弱化：仅压透明度到 45%，保留色相以兼容纸色主题。</summary>
    private static Color Disabled(Color color)
    {
        return new Color(color.r, color.g, color.b, color.a * 0.45f);
    }

    /// <summary>清空并隐藏选项容器（选项被点击后由 Manager 调用）。</summary>
    public void HideChoices()
    {
        ClearChildren(_choiceRoot.transform);
        _choiceRoot.SetActive(false);
    }

    /// <summary>
    /// 展示历史窗并逐条重建文本；speaker 空视为旁白只出正文，完成后滚到最新一条。
    /// </summary>
    public void ShowHistory(IReadOnlyList<(string speaker, string text)> entries)
    {
        // 先失活旧条目再销毁：延迟销毁期间旧条目仍参与布局，会污染归底位置
        for (int i = _historyContent.childCount - 1; i >= 0; i--)
        {
            _historyContent.GetChild(i).gameObject.SetActive(false);
        }

        ClearChildren(_historyContent);
        _historyEntries.Clear();

        Color fg = _activeConfig != null ? _activeConfig.TextColor : Color.white;
        TMP_FontAsset font = _activeConfig?.Font;
        float width = Mathf.Max(100f, ((RectTransform)_panel.transform).rect.width - 80f);

        for (int i = 0; i < entries.Count; i++)
        {
            string speaker = entries[i].speaker;
            var entry = CreateText($"Entry_{i}", _historyContent, font);
            entry.text = string.IsNullOrEmpty(speaker) ? entries[i].text : $"【{speaker}】{entries[i].text}";
            entry.fontSize = 22;
            entry.color = fg;
            entry.enableWordWrapping = true;
            var element = entry.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width; // 首选宽度封顶面板宽-80，超长自动折行
            _historyEntries.Add(entry);
        }

        _historyRoot.SetActive(true);
        // 同帧刚填充的布局尚未计算，直接归底会拿到旧高度——先强制重建再归底（最新在最下）
        LayoutRebuilder.ForceRebuildLayoutImmediate(_historyContent);
        _historyScroll.verticalNormalizedPosition = 0f;
    }

    public void HideHistory()
    {
        _historyRoot.SetActive(false);
    }

    public bool IsHistoryVisible => _historyRoot.activeSelf;

    /// <summary>AUTO 播放角标显隐（角标常驻 Panel，独立于 SetContinueVisible）。</summary>
    public void SetAutoBadgeVisible(bool visible)
    {
        _autoBadge.gameObject.SetActive(visible);
    }

    /// <summary>显示交互提示（触发器靠近范围内、未播放时），如「按 E 交谈」。</summary>
    public void ShowInteractPrompt(string text)
    {
        _interactPrompt.text = text;
        _interactPromptRoot.SetActive(true);
    }

    public void HideInteractPrompt()
    {
        _interactPromptRoot.SetActive(false);
    }

    private static void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(root.GetChild(i).gameObject); // Object 因 using System 有歧义，全限定；EditMode 下延迟销毁，可接受
        }
    }

    /// <summary>每帧驱动（由 Manager.Update 调用）："▼"上下呼吸。</summary>
    public void Tick(float time)
    {
        if (_arrow == null || !_arrow.gameObject.activeSelf)
        {
            return;
        }

        float breath = (Mathf.Sin(time * 4f) + 1f) * 0.5f; // 0..1
        _arrow.rectTransform.anchoredPosition = new Vector2(-16, 26f + breath * 6f);
    }

    private static void ApplySprite(Image image, Sprite sprite, Color fallbackColor)
    {
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
        }
        else
        {
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = fallbackColor;
        }
    }

    private static Image CreateImage(string goName, Transform parent)
    {
        var go = new GameObject(goName, typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateText(string goName, Transform parent, TMP_FontAsset font)
    {
        var go = new GameObject(goName, typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null)
        {
            tmp.font = font; // 显式赋值，不依赖 TMP Settings 默认字体
        }

        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>cfg 为 null（未跑 Setup）时的中性样式，让 ApplyStyle 走统一赋值路径。</summary>
    private static DialogueUIConfig NullConfig
    {
        get
        {
            if (_nullConfig == null)
            {
                _nullConfig = ScriptableObject.CreateInstance<DialogueUIConfig>();
                _nullConfig.hideFlags = HideFlags.HideAndDontSave; // 不落盘、不显示
            }

            return _nullConfig;
        }
    }

    private static DialogueUIConfig _nullConfig;
}
