using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行时构建的对话 UI（纸质风格 + 左侧角色头像）。由 DialogueManager 持有的普通类，非 MonoBehaviour。
/// 层级：DialogueCanvas > Panel(纸面) > PortraitFrame(头像框) > PortraitImage/PortraitInitial
///                                    > NamePlate(姓名牌) > NameText / BodyText / ContinueArrow。
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
    private GameObject _namePlate;
    private TextMeshProUGUI _name;
    private TextMeshProUGUI _body;
    private TextMeshProUGUI _arrow;

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
        }

        _name.color = cfg.SpeakerColor;
        _body.color = cfg.TextColor;
        _arrow.color = cfg.TextColor;
        _portraitInitial.color = cfg.TextColor;

        // 换字体后旧网格可能残留（面板 inactive 时普通刷新不重建），强制重建双保险
        _body.ForceMeshUpdate(true);
    }

    public void SetContinueVisible(bool visible)
    {
        _arrow.gameObject.SetActive(visible);
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
