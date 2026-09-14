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
/// 配置了 layoutPrefab 时改为实例化预制体并按 DialogueUILayoutContract 的节点名解析引用，
/// 代码层级退居默认/回退实现（详见 TryBuildFromPrefab）。
/// </summary>
public class DialogueUI
{
    // 布局常量（视觉验证后可微调）
    private const float PortraitSize = 104f;
    private const float PortraitMarginLeft = 20f;
    private const float PortraitInset = 18f;   // 头像内缩（略小于 border 22，纸框花纹不全吃掉）
    private const float BodyDefaultX = 28f;    // 无头像时正文左边距
    private const float BodyIndentX = 140f;    // 有头像时正文左边距
    private const float BodyIndentShift = BodyIndentX - BodyDefaultX; // 头像出现时的相对让位量（authored 基准上加的增量）
    private const float ArrowBreathRange = 6f; // "▼"呼吸幅度（authored 位置上的相对偏移）

    // 选项无配置兜底色（代码生成分支与模板工厂共用，防两处漂移）
    private static readonly Color ChoiceBgFallback = new Color(0.96f, 0.91f, 0.82f, 0.95f);
    private static readonly Color ChoiceFgFallback = new Color(0.35f, 0.22f, 0.12f, 1f);

    // authored 布局基准：Build 成功后快照策划摆放值，运行时改写一律写成「基准 + 相对量」，
    // 保证预制体模式下 SetSpeaker 的正文让位与 Tick 的箭头呼吸不再冲掉策划摆放
    private bool _authoredCaptured;
    private Vector2 _bodyOffsetMinAuthored;
    private Vector2 _arrowPositionAuthored;

    // authored 底图（姓名牌/头像框/全屏背景）：配置贴图未设时以预制体 Image 上自带的贴图/着色为准，
    // 两者皆无 → 底图不渲染（ApplyBackdrop）。代码默认路径无贴图 → 纯文字/裸头像观感
    private Sprite _plateSpriteAuthored;
    private Color _plateColorAuthored = Color.white;
    private Sprite _frameSpriteAuthored;
    private Color _frameColorAuthored = Color.white;
    private Sprite _bgSpriteAuthored;
    private Color _bgColorAuthored = Color.white;

    private GameObject _panel;
    private GameObject _choiceRoot;
    private GameObject _namePlate;

    // 动态条目模板（可选，预制体模式解析；未激活常驻容器下，选项按钮/历史条目按需克隆替代代码生成。
    // 被误销毁时 fake-null 判空成立，自动回退代码生成）
    private GameObject _choiceTemplate;
    private GameObject _entryTemplate;
    private TextMeshProUGUI _name;
    private TextMeshProUGUI _body;
    private TextMeshProUGUI _arrow;

    // 历史窗（HistoryRoot 全屏黑底 > Title / Scroll > Viewport > Content / Hint）
    private GameObject _historyRoot;
    private RectTransform _historyContent;
    private ScrollRect _historyScroll;
    private TextMeshProUGUI _historyTitle;
    private TextMeshProUGUI _historyHint;
    private readonly List<(TextMeshProUGUI text, bool templated)> _historyEntries
        = new List<(TextMeshProUGUI, bool)>(); // ApplyStyle 就地重刷；模板条目只刷字体不刷色
    private TextMeshProUGUI _autoBadge;

    // 交互提示（InteractPrompt：纸面小框 + 文本，挂 Canvas 与 Panel 平级——播放态显示 Panel、非播放态显示它）
    private GameObject _interactPromptRoot;
    private Image _interactPromptImage;
    private TextMeshProUGUI _interactPrompt;

    // ApplyStyle 就地更新目标
    private Image _backgroundImage;
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
            return; // 已构建
        }

        // 布局预制体优先：策划在编辑器里自由布置的层级按契约节点名解析引用；
        // 缺必需节点 → 报错指路并回退下面的代码默认布局，游戏永不因改 UI 而崩
        if (cfg != null && cfg.LayoutPrefab != null && TryBuildFromPrefab(parent, cfg))
        {
            return;
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

        // ---- Background：全屏背景（首个子节点 = 最底层；未配置贴图时整体不渲染，露出游戏画面） ----
        _backgroundImage = CreateImage("Background", canvasGo.transform);
        var bgRect = _backgroundImage.rectTransform;
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

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
        EnsureEventSystem(canvasGo.transform);

        _panel.SetActive(false); // 默认隐藏（只隐藏 Panel；Manager 常驻，规避自激活陷阱）
        _backgroundImage.gameObject.SetActive(false); // 背景随面板走：非播放态不遮挡游戏画面

        CaptureAuthoredLayout(); // 快照代码默认值作运行时改写基准

        ApplyStyle(cfg); // 样式赋值单点（含 cfg 为 null 的兜底）
    }

    // ---------- 布局预制体路径（cfg.LayoutPrefab 非空时取代代码层级）----------

    /// <summary>
    /// 实例化布局预制体并接管：契约校验 → 按名解析引用 → 功能组件自愈 → 动态态归零。
    /// 校验不过返回 false（实例已清理、问题清单已报错），Build 回退代码默认布局。
    /// </summary>
    private bool TryBuildFromPrefab(Transform parent, DialogueUIConfig cfg)
    {
        GameObject instance = UnityEngine.Object.Instantiate(cfg.LayoutPrefab, parent, false);
        instance.name = "DialogueCanvas";

        var optionalNotes = new List<string>();
        var problems = DialogueUILayoutContract.Validate(instance, optionalNotes);
        if (problems.Count > 0)
        {
            Debug.LogError("[Dialogue] 布局预制体缺必需节点，已回退代码默认布局：\n" + string.Join("\n", problems)
                + "\n排查：DialogueUIConfig 的「布局预制体」字段旁点「检查布局预制体」，或清空字段恢复默认。");
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance); // 编辑模式（预览窗）不允许 Destroy
            }

            return false;
        }

        if (optionalNotes.Count > 0)
        {
            Debug.Log("[Dialogue] 布局预制体按降级模式运行：" + string.Join("；", optionalNotes));
        }

        ResolveLayoutReferences(instance.transform);
        HealFunctionalComponents(instance);
        EnsureBackgroundNode(instance.transform); // 旧版本导出的预制体没有 Background，补建后配置背景才生效
        CaptureAuthoredLayout(); // 快照策划摆放值作运行时改写基准（让位/呼吸只做相对偏移）

        // 动态态归零（与代码路径一致：Manager 常驻，默认全隐藏）
        _panel.SetActive(false);
        if (_backgroundImage != null)
        {
            _backgroundImage.gameObject.SetActive(false);
        }
        _choiceRoot.SetActive(false);
        _historyRoot.SetActive(false);
        _interactPromptRoot.SetActive(false);
        if (_autoBadge != null)
        {
            _autoBadge.gameObject.SetActive(false);
        }

        EnsureEventSystem(instance.transform);
        ApplyStyle(cfg); // 样式仍归配置管（字体/颜色/九宫格贴图），预制体只管布局与装饰
        return true;
    }

    /// <summary>按契约节点名把实例解析进字段。可选节点缺失（或组件被删）一律解析为 null，后续判空降级。</summary>
    private void ResolveLayoutReferences(Transform root)
    {
        var background = ResolveNodeWithComponent(root, "Background", typeof(Image));
        _backgroundImage = background != null ? background.GetComponent<Image>() : null;

        _panel = DialogueUILayoutContract.Find(root, "Panel").gameObject;
        _panelImage = _panel.GetComponent<Image>(); // 面板底图可由子节点承担，缺 Image 只是不吃贴图

        var frame = ResolveNodeWithComponent(root, "PortraitFrame", typeof(Image));
        _portraitRoot = frame != null ? frame.gameObject : null;
        _portraitFrameImage = frame != null ? frame.GetComponent<Image>() : null;
        _portraitImage = ResolveComponent<Image>(root, "PortraitImage");
        _portraitInitial = ResolveComponent<TextMeshProUGUI>(root, "PortraitInitial");

        var plate = ResolveNodeWithComponent(root, "NamePlate", typeof(Image));
        _namePlate = plate != null ? plate.gameObject : null;
        _plateImage = plate != null ? plate.GetComponent<Image>() : null;
        _name = ResolveComponent<TextMeshProUGUI>(root, "NameText");
        _body = ResolveComponent<TextMeshProUGUI>(root, "BodyText");
        _arrow = ResolveComponent<TextMeshProUGUI>(root, "ContinueArrow");
        _autoBadge = ResolveComponent<TextMeshProUGUI>(root, "AutoBadge");

        _choiceRoot = DialogueUILayoutContract.Find(root, "ChoiceRoot").gameObject;

        _historyRoot = DialogueUILayoutContract.Find(root, "HistoryRoot").gameObject;
        _historyTitle = ResolveComponent<TextMeshProUGUI>(root, "Title");
        _historyHint = ResolveComponent<TextMeshProUGUI>(root, "Hint");
        _historyContent = (RectTransform)DialogueUILayoutContract.Find(root, "Content");
        _historyScroll = DialogueUILayoutContract.Find(root, "HistoryScroll").GetComponent<ScrollRect>();

        _interactPromptRoot = DialogueUILayoutContract.Find(root, "InteractPrompt").gameObject;
        _interactPromptImage = _interactPromptRoot.GetComponent<Image>();
        _interactPrompt = ResolveComponent<TextMeshProUGUI>(root, "PromptText");

        ResolveTemplates(root);
    }

    /// <summary>
    /// 解析动态条目模板（可选）：ChoiceTemplate 需 Button 且子树内找得到 TMP 文本，EntryTemplate 根即 TMP；
    /// 不可用（缺失/缺件/无文本）一律置 null 降级代码生成。模板必须未激活，激活会被当成多余条目。
    /// </summary>
    private void ResolveTemplates(Transform root)
    {
        var choiceNode = ResolveNodeWithComponent(root, "ChoiceTemplate", typeof(Button));
        if (choiceNode != null && FindTemplateLabel(choiceNode) == null)
        {
            Debug.LogWarning("[Dialogue] ChoiceTemplate 内未找到 TMP 文本（建议子节点命名 Label），选项退回代码生成。");
            choiceNode = null;
        }

        _choiceTemplate = choiceNode != null ? choiceNode.gameObject : null;

        var entryNode = ResolveNodeWithComponent(root, "EntryTemplate", typeof(TextMeshProUGUI));
        _entryTemplate = entryNode != null ? entryNode.gameObject : null;

        NormalizeTemplateActive(_choiceTemplate, "ChoiceTemplate");
        NormalizeTemplateActive(_entryTemplate, "EntryTemplate");
    }

    /// <summary>模板文本取用：子树内名「Label」的 TMP 优先，任意 TMP 兜底；皆无返回 null（降级代码生成）。</summary>
    private static TextMeshProUGUI FindTemplateLabel(Transform node)
    {
        var labelNode = DialogueUILayoutContract.Find(node, "Label");
        var label = labelNode != null ? labelNode.GetComponent<TextMeshProUGUI>() : null;
        if (label == null)
        {
            label = node.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        return label;
    }

    /// <summary>模板应为未激活（激活会被布局组当成多余条目渲染）：激活则警告并归一化。</summary>
    private static void NormalizeTemplateActive(GameObject template, string name)
    {
        if (template != null && template.activeSelf)
        {
            Debug.LogWarning($"[Dialogue] 布局预制体的 {name} 模板应为未激活，已自动停用（激活状态会被当成多余条目）。");
            template.SetActive(false);
        }
    }

    /// <summary>找节点且必须挂指定组件，缺一按「节点不存在」处理（与契约的可选降级语义一致）。</summary>
    private static Transform ResolveNodeWithComponent(Transform root, string name, Type component)
    {
        var node = DialogueUILayoutContract.Find(root, name);
        return node != null && node.GetComponent(component) != null ? node : null;
    }

    private static T ResolveComponent<T>(Transform root, string name) where T : Component
    {
        var node = DialogueUILayoutContract.Find(root, name);
        return node != null ? node.GetComponent<T>() : null;
    }

    /// <summary>
    /// 实例上的功能组件自愈（只补缺/接线，不覆盖已有参数）：Canvas 三件套、滚动裁剪、
    /// 布局组与高度自适应、ScrollRect 引用。策划改布局不该有机会改坏功能。
    /// </summary>
    private static void HealFunctionalComponents(GameObject canvasGo)
    {
        if (canvasGo.GetComponent<Canvas>() == null)
        {
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
        }

        if (canvasGo.GetComponent<CanvasScaler>() == null)
        {
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (canvasGo.GetComponent<GraphicRaycaster>() == null)
        {
            canvasGo.AddComponent<GraphicRaycaster>();
        }

        HealVerticalContainer(DialogueUILayoutContract.Find(canvasGo.transform, "ChoiceRoot"));
        var content = DialogueUILayoutContract.Find(canvasGo.transform, "Content");
        HealVerticalContainer(content);

        var viewport = DialogueUILayoutContract.Find(canvasGo.transform, "Viewport");
        if (viewport != null && viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null)
        {
            viewport.gameObject.AddComponent<RectMask2D>(); // 已有 Mask（带图模板）则尊重
        }

        var scrollNode = DialogueUILayoutContract.Find(canvasGo.transform, "HistoryScroll");
        var scroll = scrollNode != null ? scrollNode.GetComponent<ScrollRect>() : null;
        if (scroll != null)
        {
            // 引用接线是功能不是美观：无条件接好，策划无需在 Inspector 里手连
            scroll.viewport = (RectTransform)viewport;
            scroll.content = (RectTransform)content;
            scroll.horizontal = false;
            if (scrollNode.GetComponent<Image>() == null)
            {
                var raycastCatcher = scrollNode.gameObject.AddComponent<Image>(); // 滚轮事件的射线落点
                raycastCatcher.color = Color.clear;
            }
        }
    }

    /// <summary>
    /// 补建缺失的全屏背景节点：1.6.0 之前导出的布局预制体里没有 Background，
    /// 缺了会让配置的背景贴图静默失效。建为根的首个子节点（最底层），只建空节点——
    /// 未配置贴图时 ApplyBackdrop 不渲染，无意删掉它的策划不受影响。
    /// </summary>
    private void EnsureBackgroundNode(Transform root)
    {
        if (_backgroundImage != null)
        {
            return;
        }

        _backgroundImage = CreateImage("Background", root);
        _backgroundImage.transform.SetAsFirstSibling(); // 最底层，别盖住面板
        var rect = _backgroundImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>垂直容器自愈：布局组/高度自适应缺失才补（已有则尊重策划的间距等参数）。</summary>
    private static void HealVerticalContainer(Transform container)
    {
        if (container == null)
        {
            return;
        }

        if (container.GetComponent<VerticalLayoutGroup>() == null)
        {
            var layout = container.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
        }

        if (container.GetComponent<ContentSizeFitter>() == null)
        {
            var fitter = container.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    /// <summary>场景无 EventSystem 时自举一个（Button 点击依赖）；有则复用，避免多实例告警。</summary>
    private static void EnsureEventSystem(Transform canvasParent)
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        var eventSystemGo = new GameObject("DialogueEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        eventSystemGo.transform.SetParent(canvasParent, false);
    }

    /// <summary>
    /// 在已建好的代码默认层级上追加两个未激活的动态条目模板（导出工具调用：让出厂预制体自带
    /// 可定制的选项按钮/历史条目外观；运行时代码路径不建模板，零配置行为不变）。
    /// 模板的配色/字号归模板（改它即改观感），字体资产仍归配置（克隆时统一覆盖）。
    /// </summary>
    public static void AppendDefaultTemplates(Transform canvasRoot)
    {
        // ChoiceTemplate：选项按钮模板（Button + 底图 + Label 文本），挂在 ChoiceRoot 下
        if (DialogueUILayoutContract.Find(canvasRoot, "ChoiceTemplate") == null)
        {
            var choiceRoot = DialogueUILayoutContract.Find(canvasRoot, "ChoiceRoot");
            if (choiceRoot != null)
            {
                var template = new GameObject("ChoiceTemplate", typeof(Image), typeof(Button), typeof(LayoutElement));
                template.transform.SetParent(choiceRoot, false);
                var image = template.GetComponent<Image>();
                image.color = ChoiceBgFallback;
                image.raycastTarget = true; // 与代码生成的选项一致：底图参与射线保证可点
                var element = template.GetComponent<LayoutElement>();
                element.minHeight = 40f;
                element.preferredWidth = 360f;

                var label = CreateText("Label", template.transform, null);
                label.text = "选项";
                label.fontSize = 24;
                label.alignment = TextAlignmentOptions.Center;
                label.color = ChoiceFgFallback;
                label.raycastTarget = true;
                var labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                template.SetActive(false); // 模板常驻未激活，条目由克隆生成
            }
        }

        // EntryTemplate：历史条目模板（根即 TMP 文本），挂在 Content 下
        if (DialogueUILayoutContract.Find(canvasRoot, "EntryTemplate") == null)
        {
            var content = DialogueUILayoutContract.Find(canvasRoot, "Content");
            if (content != null)
            {
                var entry = CreateText("EntryTemplate", content, null);
                entry.text = "历史条目";
                entry.fontSize = 22;
                entry.enableWordWrapping = true;
                var element = entry.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = 0f; // 留空：克隆期未设置时按「面板宽 - 80」封顶，防横向溢出
                entry.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>快照 authored 布局基准（两条 Build 路径尾部各调一次）：正文 offsetMin、箭头位置、可选底图。</summary>
    private void CaptureAuthoredLayout()
    {
        if (_body != null)
        {
            _bodyOffsetMinAuthored = _body.rectTransform.offsetMin;
        }

        if (_arrow != null)
        {
            _arrowPositionAuthored = _arrow.rectTransform.anchoredPosition;
        }

        if (_plateImage != null)
        {
            _plateSpriteAuthored = _plateImage.sprite;
            _plateColorAuthored = _plateImage.color;
        }

        if (_portraitFrameImage != null)
        {
            _frameSpriteAuthored = _portraitFrameImage.sprite;
            _frameColorAuthored = _portraitFrameImage.color;
        }

        if (_backgroundImage != null)
        {
            _bgSpriteAuthored = _backgroundImage.sprite;
            _bgColorAuthored = _backgroundImage.color;
        }

        _authoredCaptured = true;
    }

    public void Show()
    {
        _panel.SetActive(true);
        if (_backgroundImage != null)
        {
            _backgroundImage.gameObject.SetActive(true);
        }
    }

    public void Hide()
    {
        _panel.SetActive(false);
        if (_backgroundImage != null)
        {
            _backgroundImage.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 设置说话人：speaker 资产优先（头像 + 名字颜色覆盖），否则回退旧纯文本名；两者皆空 = 旁白。
    /// </summary>
    public void SetSpeaker(SpeakerAsset speaker, string legacyName)
    {
        string display = speaker != null ? speaker.GetDisplayName() : legacyName;
        bool has = !string.IsNullOrEmpty(display);

        // 姓名牌整体可选：节点被删/缺 NameText 时整块跳过（默认布局引用恒全）
        if (_namePlate != null)
        {
            bool show = has && _name != null;
            _namePlate.SetActive(show);
            if (show)
            {
                _name.text = display;
            }
        }

        // 头像区：无 speaker（旁白/旧文本）时整体隐藏；内部件缺失则对应功能静默降级
        bool portraitVisible = speaker != null && _portraitRoot != null;
        if (_portraitRoot != null)
        {
            _portraitRoot.SetActive(portraitVisible);
            if (portraitVisible)
            {
                bool hasPortrait = speaker.Portrait != null;
                if (_portraitImage != null)
                {
                    _portraitImage.gameObject.SetActive(hasPortrait);
                    if (hasPortrait)
                    {
                        _portraitImage.sprite = speaker.Portrait;
                    }
                }

                if (_portraitInitial != null)
                {
                    _portraitInitial.gameObject.SetActive(!hasPortrait);
                    if (!hasPortrait && display.Length > 0)
                    {
                        // 取首字；emoji 代理对会截半个字符——中英文名均为单 char，接受
                        _portraitInitial.text = display.Substring(0, 1);
                    }
                }
            }
        }

        // 名字颜色：speaker 覆盖 > 样式默认
        Color fallback = _activeConfig != null ? _activeConfig.SpeakerColor : Color.white;
        SetColor(_name, speaker != null ? speaker.ResolveNameColor(fallback) : fallback);

        // 正文让位：以 authored 基准为底、头像出现时右移固定增量（策划的 y 边距一并被尊重）；
        // 未捕获（Build 前被调，防御）走旧常量
        if (_authoredCaptured)
        {
            _body.rectTransform.offsetMin = _bodyOffsetMinAuthored + new Vector2(portraitVisible ? BodyIndentShift : 0f, 0f);
        }
        else
        {
            _body.rectTransform.offsetMin = new Vector2(portraitVisible ? BodyIndentX : BodyDefaultX, 14f);
        }
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

        // 预制体布局下可选节点可能不存在（或缺组件），赋值统一判空降级；默认布局引用恒全
        ApplySprite(_panelImage, cfg.PanelSprite, new Color(0, 0, 0, 0.75f));
        // 全屏背景/姓名牌/头像框底图：配置贴图 > 预制体自带贴图 > 整块不渲染（露出下层画面）
        ApplyBackdrop(_backgroundImage, cfg.BackgroundSprite, _bgSpriteAuthored, _bgColorAuthored);
        ApplyBackdrop(_plateImage, cfg.NameSprite, _plateSpriteAuthored, _plateColorAuthored);
        ApplyBackdrop(_portraitFrameImage, cfg.PortraitFrameSprite, _frameSpriteAuthored, _frameColorAuthored);

        SetFont(_name, cfg.Font);
        SetFont(_body, cfg.Font);
        SetFont(_arrow, cfg.Font);
        SetFont(_portraitInitial, cfg.Font);
        SetFont(_historyTitle, cfg.Font);
        SetFont(_historyHint, cfg.Font);
        SetFont(_autoBadge, cfg.Font);

        SetColor(_name, cfg.SpeakerColor);
        SetColor(_body, cfg.TextColor);
        SetColor(_arrow, cfg.TextColor);
        SetColor(_portraitInitial, cfg.TextColor);

        // 历史窗与角标跟随换肤（黑底上若 cfg.TextColor 偏暗则由配置方自行调亮，此处不做二次加工）
        Color historyFg = cfg.TextColor;
        SetColor(_historyTitle, historyFg);
        SetColor(_historyHint, historyFg);
        SetColor(_autoBadge, cfg.SpeakerColor);

        // 交互提示跟随换肤（与面板同款纸面/字体/文字色）
        ApplySprite(_interactPromptImage, cfg.PanelSprite, new Color(0, 0, 0, 0.75f));
        SetFont(_interactPrompt, cfg.Font);
        SetColor(_interactPrompt, cfg.TextColor);
        foreach (var entry in _historyEntries)
        {
            SetFont(entry.text, cfg.Font);
            if (!entry.templated)
            {
                entry.text.color = historyFg; // 模板条目配色归模板（美术接管），换肤不动其颜色
            }
        }

        // 换字体后旧网格可能残留（面板 inactive 时普通刷新不重建），强制重建双保险
        _body.ForceMeshUpdate(true);
    }

    public void SetContinueVisible(bool visible)
    {
        if (_arrow != null)
        {
            _arrow.gameObject.SetActive(visible);
        }
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
    /// 有 ChoiceTemplate 模板时按钮按模板克隆（配色/字号归模板），否则代码生成（配色归配置）。
    /// </summary>
    public void ShowChoices(IReadOnlyList<DialogueChoice> choices, Action<int> onSelected, IReadOnlyList<bool> enabledFlags)
    {
        ClearChildren(_choiceRoot.transform, TemplateTransform(_choiceTemplate));

        if (_choiceTemplate != null)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                // 缺项/null 一律视为可用，避免标记列表长度不一致时误伤
                bool enabled = enabledFlags != null && i < enabledFlags.Count ? enabledFlags[i] : true;
                CloneChoiceFromTemplate(i, choices[i].Text, enabled, onSelected);
            }

            _choiceRoot.SetActive(true);
            return;
        }

        Color bg = _activeConfig != null ? _activeConfig.ChoiceColor : ChoiceBgFallback;
        Color fg = _activeConfig != null ? _activeConfig.ChoiceTextColor : ChoiceFgFallback;

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

    /// <summary>
    /// 按模板克隆一个选项按钮：结构/配色/字号随模板（美术接管）；文本/回调/置灰归代码。
    /// 模板缺功能件只补缺不覆盖（沿用自愈哲学）。
    /// </summary>
    private void CloneChoiceFromTemplate(int index, string text, bool enabled, Action<int> onSelected)
    {
        var clone = UnityEngine.Object.Instantiate(_choiceTemplate.gameObject, _choiceRoot.transform, false);
        clone.name = $"Choice_{index}";
        clone.SetActive(true); // 源模板未激活，克隆需显式激活

        var element = clone.GetComponent<LayoutElement>();
        if (element == null)
        {
            element = clone.AddComponent<LayoutElement>();
        }

        if (element.minHeight <= 0f)
        {
            element.minHeight = 40f;
        }

        if (element.preferredWidth <= 0f)
        {
            element.preferredWidth = 360f;
        }

        var label = FindTemplateLabel(clone.transform);
        if (label != null)
        {
            label.text = text;
            SetFont(label, _activeConfig != null ? _activeConfig.Font : null); // 字体资产归配置，为空尊重模板
        }

        if (enabled)
        {
            // 不 RemoveAllListeners：策划在模板 Button 上挂的持久监听（如点击音效）随克隆保留并先触发
            clone.GetComponent<Button>().onClick.AddListener(() => onSelected?.Invoke(index));
        }
        else // 置灰项干脆不挂回调：点击天然无效；压透明度不改色相，天然兼容模板配色
        {
            foreach (var graphic in clone.GetComponentsInChildren<Graphic>(true))
            {
                graphic.color = Disabled(graphic.color);
            }
        }
    }

    /// <summary>清空并隐藏选项容器（选项被点击后由 Manager 调用；模板保留）。</summary>
    public void HideChoices()
    {
        ClearChildren(_choiceRoot.transform, TemplateTransform(_choiceTemplate));
        _choiceRoot.SetActive(false);
    }

    /// <summary>
    /// 展示历史窗并逐条重建文本；speaker 空视为旁白只出正文，完成后滚到最新一条。
    /// 有 EntryTemplate 模板时条目按模板克隆（配色/字号归模板），否则代码生成（配色归配置）。
    /// </summary>
    public void ShowHistory(IReadOnlyList<(string speaker, string text)> entries)
    {
        // 先失活旧条目再销毁：销毁期间旧条目仍参与布局，会污染归底位置（对未激活模板是 no-op）
        for (int i = _historyContent.childCount - 1; i >= 0; i--)
        {
            _historyContent.GetChild(i).gameObject.SetActive(false);
        }

        ClearChildren(_historyContent, TemplateTransform(_entryTemplate));
        _historyEntries.Clear();

        bool templated = _entryTemplate != null;
        Color fg = _activeConfig != null ? _activeConfig.TextColor : Color.white;
        TMP_FontAsset font = _activeConfig?.Font;
        float width = Mathf.Max(100f, ((RectTransform)_panel.transform).rect.width - 80f);

        for (int i = 0; i < entries.Count; i++)
        {
            string speaker = entries[i].speaker;
            string text = string.IsNullOrEmpty(speaker) ? entries[i].text : $"【{speaker}】{entries[i].text}";
            TextMeshProUGUI entry;

            if (templated)
            {
                entry = UnityEngine.Object.Instantiate(_entryTemplate.gameObject, _historyContent, false)
                    .GetComponent<TextMeshProUGUI>();
                entry.gameObject.name = $"Entry_{i}";
                entry.gameObject.SetActive(true); // 源模板未激活，克隆需显式激活
                entry.text = text;
                SetFont(entry, font); // 字体资产归配置；配色/字号随模板

                var element = entry.GetComponent<LayoutElement>();
                if (element == null)
                {
                    element = entry.gameObject.AddComponent<LayoutElement>();
                }

                if (element.preferredWidth <= 0f)
                {
                    element.preferredWidth = width; // 模板未给宽度才封顶「面板宽-80」，防横向溢出
                }
            }
            else
            {
                entry = CreateText($"Entry_{i}", _historyContent, font);
                entry.text = text;
                entry.fontSize = 22;
                entry.color = fg;
                entry.enableWordWrapping = true;
                var element = entry.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = width; // 首选宽度封顶面板宽-80，超长自动折行
            }

            _historyEntries.Add((entry, templated));
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
        if (_autoBadge != null)
        {
            _autoBadge.gameObject.SetActive(visible);
        }
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

    /// <summary>模板保护用 transform（显式判空取值，Unity fake-null 下 ?. 不可靠）。</summary>
    private static Transform TemplateTransform(GameObject template)
    {
        return template != null ? template.transform : null;
    }

    /// <summary>
    /// 逆序清空子节点；keep（通常为条目模板）自身或其所在子树跳过——模板常驻容器下，清条目不得误删模板。
    /// EditMode（测试/预览窗）用 DestroyImmediate 保证销毁确定性，Play 模式仍走帧末延迟销毁。
    /// </summary>
    private static void ClearChildren(Transform root, Transform keep = null)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var child = root.GetChild(i);
            if (keep != null && IsSelfOrAncestorOf(child, keep))
            {
                continue;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(child.gameObject); // Object 因 using System 有歧义，全限定
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }
    }

    /// <summary>node 是否为 target 的自身或祖先（销毁 node 会连带 target，需跳过）。</summary>
    private static bool IsSelfOrAncestorOf(Transform node, Transform target)
    {
        while (target != null)
        {
            if (target == node)
            {
                return true;
            }

            target = target.parent;
        }

        return false;
    }

    /// <summary>每帧驱动（由 Manager.Update 调用）："▼"上下呼吸（在 authored 位置上做相对偏移）。</summary>
    public void Tick(float time)
    {
        if (_arrow == null || !_arrow.gameObject.activeSelf || !_authoredCaptured)
        {
            return;
        }

        float breath = (Mathf.Sin(time * 4f) + 1f) * 0.5f; // 0..1
        _arrow.rectTransform.anchoredPosition = _arrowPositionAuthored + new Vector2(0f, breath * ArrowBreathRange);
    }

    private static void ApplySprite(Image image, Sprite sprite, Color fallbackColor)
    {
        if (image == null)
        {
            return; // 预制体布局里该节点可选拄件，缺 Image 只是不吃贴图
        }

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

    /// <summary>
    /// 可选底图（全屏背景/姓名牌/头像框）：配置贴图 > 预制体自带贴图（authored）> 不渲染。
    /// 未设置时只关 Graphic 不关 GameObject——名字文本/头像是子物件，照常显示。
    /// </summary>
    private static void ApplyBackdrop(Image image, Sprite cfgSprite, Sprite authoredSprite, Color authoredColor)
    {
        if (image == null)
        {
            return; // 预制体布局里该节点可选拄件，缺 Image 无事可做
        }

        if (cfgSprite != null)
        {
            image.sprite = cfgSprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.enabled = true;
        }
        else if (authoredSprite != null)
        {
            image.sprite = authoredSprite;
            image.type = Image.Type.Sliced; // 无九宫格 border 的 Sliced 视觉等价 Simple
            image.color = authoredColor;    // 保留策划着色
            image.enabled = true;
        }
        else
        {
            image.enabled = false; // 未设置底图 → 不显示（不画兜底色块）
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

    /// <summary>判空赋字体/颜色：预制体布局的可选节点可能解析为 null（默认布局恒非空）。</summary>
    private static void SetFont(TextMeshProUGUI tmp, TMP_FontAsset font)
    {
        if (tmp != null && font != null)
        {
            tmp.font = font;
        }
    }

    private static void SetColor(TextMeshProUGUI tmp, Color color)
    {
        if (tmp != null)
        {
            tmp.color = color;
        }
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
