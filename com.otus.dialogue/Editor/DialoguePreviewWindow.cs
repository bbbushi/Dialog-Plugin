using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 对话免 Play 试玩窗口（IMGUI 实现）。
/// 推进语义与运行时同源——线性走 DialogueAsset.GetNext()，选项走 SelectChoice()（HasChoices 判定），预览行为永不与运行时漂移。
/// 只读资产、零序列化副作用；语速/配色读 DialogueUIConfig，观感接近运行时（非逐像素一致）。
/// </summary>
public class DialoguePreviewWindow : EditorWindow
{
    private enum PreviewState { Idle, Typing, WaitingAdvance, Finished }

    private static readonly Regex RichTextTag = new Regex("<[^>]+>", RegexOptions.Compiled);

    private DialogueAsset _asset;
    private DialogueNode _current;
    private PreviewState _state = PreviewState.Idle;
    private readonly DialogueVariables _vars = new DialogueVariables(); // 条件/赋值与运行时同语义（跨对话记忆，不随 StartPlayback 重置）
    private float _visibleChars;
    private int _totalChars;
    private readonly List<DialogueNode> _visitedChain = new List<DialogueNode>();
    private double _lastTime;
    private float _baseCps = 30f;
    private float _speedMultiplier = 1f;
    private bool _followSelection = true;
    private int _lastNodeCount = -1;

    // 样式（缓存；_styleConfig 变化时整套重建）
    private GUIStyle _panelStyle;
    private GUIStyle _nameStyle;
    private GUIStyle _bodyStyle;
    private GUIStyle _arrowStyle;
    private GUIStyle _pathStyle;
    private GUIStyle _portraitStyle;
    private GUIStyle _initialStyle;
    private DialogueUIConfig _styleConfig; // 当前样式（asset 覆盖 > 全局默认）
    private bool _stylesLoaded;

    [MenuItem("Dialogue/Open Preview")]
    public static void Open()
    {
        GetWindow<DialoguePreviewWindow>("对话预览");
    }

    /// <summary>Inspector "▶ 预览" 按钮入口：打开并直接开始播放指定资产。</summary>
    public static void Open(DialogueAsset asset)
    {
        var window = GetWindow<DialoguePreviewWindow>("对话预览");
        window.StartPlayback(asset);
    }

    /// <summary>自动化入口（MCP execute_code 驱动用）。</summary>
    public void StartPlayback(DialogueAsset asset)
    {
        _asset = asset;

        // 每对话样式：覆盖 > 全局默认；变化时强制重建 GUIStyle（语速同步跟随）
        _styleConfig = asset != null
            ? asset.ResolveStyle(Resources.Load<DialogueUIConfig>(DialogueManager.ConfigResourcePath))
            : null;
        _stylesLoaded = false;

        _visitedChain.Clear();
        _lastNodeCount = asset != null ? asset.Nodes.Count : -1;

        var start = asset != null ? asset.GetStartNode() : null;
        if (start == null)
        {
            _state = PreviewState.Finished;
            _current = null;
            Repaint();
            return;
        }

        EnterNode(start);
    }

    /// <summary>推进一次（与运行时点击同语义：打字中→补全；显示完→下一句/结束）。自动化同用。</summary>
    public void Advance()
    {
        if (_state == PreviewState.Typing)
        {
            _visibleChars = _totalChars;
            _state = PreviewState.WaitingAdvance;
            Repaint();
            return;
        }

        if (_state != PreviewState.WaitingAdvance)
        {
            return;
        }

        if (_current.HasChoices) // 选项节点：点击/键盘不推进，必须点选按钮（对齐运行时 Choosing）
        {
            return;
        }

        var next = _asset.GetNext(_current);
        if (next != null)
        {
            EnterNode(next);
        }
        else
        {
            _state = PreviewState.Finished;
            Repaint();
        }
    }

    /// <summary>自动化查询：对话是否已走完。</summary>
    public bool IsFinished => _state == PreviewState.Finished;

    /// <summary>自动化查询：当前已播放节点序列（路径链）。</summary>
    public IReadOnlyList<DialogueNode> VisitedChain => _visitedChain;

    /// <summary>
    /// 点选一个玩家选项（与运行时 OnChoiceSelected 同语义）：
    /// nextId 为空或断链 → LogError 并结束；否则进入目标节点（VisitedChain 记录分支路径）。自动化同用。
    /// </summary>
    public void SelectChoice(int index)
    {
        if (_state != PreviewState.WaitingAdvance || _current == null || !_current.HasChoices
            || index < 0 || index >= _current.Choices.Count)
        {
            return;
        }

        var choice = _current.Choices[index];
        try
        {
            DialogueExpression.Apply(_vars, choice.SetExpressions); // 选中先结算副作用（与运行时同语义，防双端漂移）
        }
        catch (FormatException e)
        {
            Debug.LogError($"[Dialogue] 预览：选项赋值执行失败（节点 {_current.Id}）：{e.Message}——跳过赋值继续。");
        }

        var next = string.IsNullOrEmpty(choice.NextId) ? null : _asset.FindNode(choice.NextId);
        if (next == null)
        {
            Debug.LogError($"[Dialogue] 选项 \"{choice.Text}\" 的跳转目标 \"{choice.NextId}\" 无效，对话结束。");
            _state = PreviewState.Finished;
            Repaint();
            return;
        }

        EnterNode(next);
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate; // 严格配对，防泄漏（域重载时 Unity 自动调用二者）
    }

    private void OnEditorUpdate()
    {
        double now = EditorApplication.timeSinceStartup;

        if (_state == PreviewState.Typing)
        {
            _visibleChars += (float)(now - _lastTime) * _baseCps * _speedMultiplier;
            if (Mathf.FloorToInt(_visibleChars) >= _totalChars)
            {
                _visibleChars = _totalChars;
                _state = PreviewState.WaitingAdvance;
            }

            Repaint(); // 仅打字中重绘；等待态静止（▼ 不做呼吸，避免持续重绘）
        }

        _lastTime = now;
    }

    private void OnSelectionChange()
    {
        if (!_followSelection || Selection.activeObject is not DialogueAsset asset || asset == _asset)
        {
            return;
        }

        StartPlayback(asset);
        Repaint();
    }

    private void OnGUI()
    {
        EnsureStyles(_styleConfig);

        // 键盘推进（窗口聚焦时）
        if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Space || Event.current.keyCode == KeyCode.Return))
        {
            if (_state == PreviewState.Finished && _asset != null)
            {
                StartPlayback(_asset);
            }
            else
            {
                Advance();
            }

            Event.current.Use();
        }

        DrawToolbar();

        if (_asset == null)
        {
            EditorGUILayout.HelpBox("未选择对话资产。\n选中一个 Dialogue Asset（或点击其 Inspector 的 ▶ 预览 按钮）即可开始。", MessageType.Info);
            return;
        }

        // 边策划边改：节点数变化 → 提示并自动重播
        if (_lastNodeCount >= 0 && _asset.Nodes.Count != _lastNodeCount)
        {
            EditorGUILayout.HelpBox("资产节点数已变化，自动重新播放。", MessageType.Warning);
            StartPlayback(_asset);
        }

        if (_state == PreviewState.Finished)
        {
            EditorGUILayout.HelpBox("✦ 对话结束。按空格 / Return / 点击面板重新播放。", MessageType.Info);
        }

        DrawDialoguePanel();
        EditorGUILayout.Space(4);
        DrawChoices();
        DrawPathSidebar();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("▶ 重新播放", EditorStyles.toolbarButton) && _asset != null)
            {
                StartPlayback(_asset);
            }

            GUILayout.Label("语速", EditorStyles.miniLabel);
            _speedMultiplier = GUILayout.Toolbar(
                _speedMultiplier switch { 0.5f => 0, 2f => 2, _ => 1 },
                new[] { "0.5×", "1×", "2×" }, EditorStyles.toolbarButton) switch { 0 => 0.5f, 2 => 2f, _ => 1f };

            GUILayout.FlexibleSpace();
            _followSelection = GUILayout.Toggle(_followSelection, "跟随检视", EditorStyles.miniButton);
        }
    }

    private void DrawDialoguePanel()
    {
        // 面板整体：点击 = Advance / 结束后 = 重播
        Rect panelRect = GUILayoutUtility.GetRect(0, 190, GUILayout.ExpandWidth(true));

        // 全屏背景：与运行时同语义，未设 = 不画（窗口内以面板区域为界）
        if (_styleConfig != null && _styleConfig.BackgroundSprite != null)
        {
            GUI.DrawTexture(panelRect, _styleConfig.BackgroundSprite.texture, ScaleMode.ScaleAndCrop);
        }

        GUI.Label(panelRect, GUIContent.none, _panelStyle);

        if (Event.current.type == EventType.MouseDown && panelRect.Contains(Event.current.mousePosition))
        {
            if (_state == PreviewState.Finished)
            {
                StartPlayback(_asset);
            }
            else
            {
                Advance();
            }

            Event.current.Use();
        }

        var speaker = _current != null ? _current.Speaker : null;
        string displayName = _current != null ? _current.ResolvedSpeakerName : null;

        // 头像（有 speaker 资产时显示，左侧垂直居中；无图走首字缩写）
        if (speaker != null)
        {
            var frameRect = new Rect(panelRect.x + 14, panelRect.y + (panelRect.height - 88) / 2, 88, 88);
            // 框底与运行时同语义：未配置贴图 → 不显示（头像/首字照常）
            if (_styleConfig != null && _styleConfig.PortraitFrameSprite != null)
            {
                GUI.Label(frameRect, GUIContent.none, _portraitStyle);
            }
            Rect inner = new RectOffset(12, 12, 12, 12).Remove(frameRect);
            if (speaker.Portrait != null)
            {
                GUI.DrawTexture(inner, speaker.Portrait.texture, ScaleMode.ScaleToFit); // 无 SpriteAtlas 前提
            }
            else if (!string.IsNullOrEmpty(displayName))
            {
                GUI.Label(inner, displayName.Substring(0, 1), _initialStyle); // emoji 代理对风险：中英文名单 char，接受
            }
        }

        // 姓名牌（贴面板左上；名色逐句设置：speaker 覆盖 > 样式默认——逐句属性不能烘焙进 EnsureStyles）
        if (!string.IsNullOrEmpty(displayName))
        {
            Color fallback = _styleConfig != null ? _styleConfig.SpeakerColor : Color.white;
            _nameStyle.normal.textColor = speaker != null ? speaker.ResolveNameColor(fallback) : fallback;
            var nameRect = new Rect(panelRect.x + 20, panelRect.y + 8, 200, 34);
            GUI.Label(nameRect, displayName, _nameStyle);
        }

        // 正文（打字机；有头像时左让位）
        if (_current != null)
        {
            string plain = StripRichText(_current.Text);
            int visible = Mathf.Clamp(Mathf.FloorToInt(_visibleChars), 0, plain.Length);
            int leftPad = speaker != null ? 115 : 70;
            var bodyRect = new RectOffset(leftPad, 40, 52, 34).Remove(panelRect);
            GUI.Label(bodyRect, plain.Substring(0, visible), _bodyStyle);

            // ▼ 等待推进指示（静态；有选项时不提示继续，去向待点选）
            if (_state == PreviewState.WaitingAdvance && !_current.HasChoices)
            {
                var arrowRect = new Rect(panelRect.xMax - 46, panelRect.yMax - 36, 30, 26);
                GUI.Label(arrowRect, "▼", _arrowStyle);
            }
        }
        else
        {
            GUI.Label(new RectOffset(20, 20, 20, 20).Remove(panelRect), "（空资产：没有任何节点）", EditorStyles.label);
        }
    }

    /// <summary>玩家选项按钮（画在面板下方，不遮正文）；条件不满足置灰（与运行时同判定）。</summary>
    private void DrawChoices()
    {
        if (_state != PreviewState.WaitingAdvance || _current == null || !_current.HasChoices)
        {
            return;
        }

        var choices = _current.Choices;
        for (int i = 0; i < choices.Count; i++)
        {
            bool enabled;
            try
            {
                enabled = DialogueExpression.Evaluate(_vars, choices[i].Condition);
            }
            catch (FormatException e)
            {
                Debug.LogError($"[Dialogue] 预览：选项条件解析失败（节点 {_current.Id}）：{e.Message}——按无条件显示。");
                enabled = true;
            }

            EditorGUILayout.Space(6);
            Rect rect = GUILayoutUtility.GetRect(220, 30, GUILayout.Width(220), GUILayout.Height(30));
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUI.Button(rect, $"{i + 1}. {choices[i].Text}") && enabled)
                {
                    SelectChoice(i);
                }
            }
        }
    }

    private void DrawPathSidebar()
    {
        if (_asset == null || _visitedChain.Count == 0)
        {
            return;
        }

        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            GUILayout.Label("播放路径", EditorStyles.boldLabel);

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _visitedChain.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(" → ");
                }

                sb.Append(string.IsNullOrEmpty(_visitedChain[i].Id) ? "?" : _visitedChain[i].Id);
            }

            bool isCurrentLast = _visitedChain.Count > 0 && ReferenceEquals(_visitedChain[_visitedChain.Count - 1], _current);
            GUILayout.Label(sb.ToString() + (isCurrentLast ? "（当前）" : ""), _pathStyle);

            // 预告下一句（有选项时去向未定，GetNext 的结果会被运行时忽略，不做预告防误导）
            if (_current != null && _current.HasChoices)
            {
                GUILayout.Label("下一句：由玩家选项决定", EditorStyles.miniLabel);
            }
            else
            {
                var next = _asset.GetNext(_current);
                if (next != null)
                {
                    int index = IndexOfNode(next);
                    GUILayout.Label($"下一句：{(index >= 0 ? $"#{index + 1} " : "")}{next.Id} · {Truncate(next.Text, 18)}", EditorStyles.miniLabel);
                }
                else if (_state == PreviewState.WaitingAdvance)
                {
                    GUILayout.Label("下一句：■ 对话将结束", EditorStyles.miniLabel);
                }
            }
        }
    }

    private int IndexOfNode(DialogueNode node)
    {
        var nodes = _asset.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i], node))
            {
                return i;
            }
        }

        return -1;
    }

    private void EnterNode(DialogueNode node)
    {
        _current = node;
        _visitedChain.Add(node);
        _totalChars = StripRichText(node.Text).Length;
        _visibleChars = 0f;
        _state = PreviewState.Typing;
        _lastTime = EditorApplication.timeSinceStartup;
        Repaint();
    }

    private static string StripRichText(string text)
    {
        return string.IsNullOrEmpty(text) ? string.Empty : RichTextTag.Replace(text, string.Empty);
    }

    private static string Truncate(string text, int max)
    {
        string plain = StripRichText(text);
        return plain.Length <= max ? plain : plain.Substring(0, max) + "…";
    }

    /// <summary>
    /// 从 DialogueUIConfig 构建样式：颜色对齐运行时，纸面贴图 + 九宫格 border 复刻观感。
    /// 重入安全：cfg 与当前不一致时整套重建（每对话样式切换用）。
    /// </summary>
    private void EnsureStyles(DialogueUIConfig cfg)
    {
        if (_stylesLoaded && ReferenceEquals(_styleConfig, cfg))
        {
            return;
        }

        _styleConfig = cfg;
        Font font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/simhei.ttf"); // 可选，null 回退编辑器默认（默认支持中文）

        _panelStyle = new GUIStyle(GUI.skin.box);
        _nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = false };
        _bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, richText = false };
        _arrowStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleRight };
        _pathStyle = new GUIStyle(EditorStyles.miniBoldLabel) { wordWrap = true };
        _portraitStyle = new GUIStyle(GUI.skin.box);
        _initialStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter };

        if (cfg != null)
        {
            // spriteBorder 的 (x,y,z,w) = (Left,Bottom,Right,Top)；GUIStyle.border = RectOffset(Left,Right,Top,Bottom)
            if (cfg.PanelSprite != null)
            {
                _panelStyle.normal.background = cfg.PanelSprite.texture;
                var b = cfg.PanelSprite.border;
                _panelStyle.border = new RectOffset(Mathf.RoundToInt(b.x), Mathf.RoundToInt(b.z), Mathf.RoundToInt(b.w), Mathf.RoundToInt(b.y));
            }

            if (cfg.NameSprite != null)
            {
                _nameStyle.normal.background = cfg.NameSprite.texture;
                var b = cfg.NameSprite.border;
                _nameStyle.border = new RectOffset(Mathf.RoundToInt(b.x), Mathf.RoundToInt(b.z), Mathf.RoundToInt(b.w), Mathf.RoundToInt(b.y));
                _nameStyle.padding = new RectOffset(26, 26, 8, 8);
            }

            if (cfg.PortraitFrameSprite != null)
            {
                _portraitStyle.normal.background = cfg.PortraitFrameSprite.texture;
                var b = cfg.PortraitFrameSprite.border; // 同款换算，照抄 border 会左右颠倒
                _portraitStyle.border = new RectOffset(Mathf.RoundToInt(b.x), Mathf.RoundToInt(b.z), Mathf.RoundToInt(b.w), Mathf.RoundToInt(b.y));
            }

            _nameStyle.normal.textColor = cfg.SpeakerColor;
            _bodyStyle.normal.textColor = cfg.TextColor;
            _arrowStyle.normal.textColor = cfg.TextColor;
            _initialStyle.normal.textColor = cfg.TextColor;
            _baseCps = cfg.CharactersPerSecond; // 预览语速同步跟随每对话样式
        }

        // GUI.Label 鼠标悬停时会切到样式的 hover 状态绘制（默认皮肤 hover 色是浅灰白）。
        // 共享 GUIStyleState 引用：normal 被配置色覆盖/逐句改色时 hover 自动跟随，避免悬停变白看不清
        _panelStyle.hover = _panelStyle.normal;
        _nameStyle.hover = _nameStyle.normal;
        _bodyStyle.hover = _bodyStyle.normal;
        _arrowStyle.hover = _arrowStyle.normal;
        _initialStyle.hover = _initialStyle.normal;

        if (font != null)
        {
            _nameStyle.font = font;
            _bodyStyle.font = font;
            _arrowStyle.font = font;
            _initialStyle.font = font;
        }

        _stylesLoaded = true;
    }
}
