using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 对话运行时驱动：状态机 + 打字机 + 输入推进。
/// 懒创建自举单例（场景零摆放），UI 由 DialogueUI 在运行时构建。
/// 打字机用 TMP 的 maxVisibleCharacters 递增：全文只设一次，布局只算一次，
/// 富文本标签不计入计数，将来加 <color> 等标记不破坏打字机。
/// 变量/历史/已读/快照是会话层状态：跨对话保留，ResetSession 统一清。
/// </summary>
public class DialogueManager : MonoBehaviour
{
    /// <summary>UI 配置的 Resources 相对路径（预览窗口等编辑器工具复用，防路径漂移）。</summary>
    public const string ConfigResourcePath = "DialogueUIConfig";
    private const float DefaultCharactersPerSecond = 30f;

    private enum State { Idle, Typing, WaitingAdvance, Choosing }

    public static DialogueManager Instance { get; private set; }

    /// <summary>静态便捷查询（外部触发器用；实例可能尚未创建）。</summary>
    public static bool IsAnyPlaying => Instance != null && Instance.IsPlaying;

    public bool IsPlaying { get; private set; }

    /// <summary>对话开始（参数：对话资产）。</summary>
    public event Action<DialogueAsset> DialogueStarted;

    /// <summary>每句开始（参数：当前节点）。为将来任务/演出系统留缝。</summary>
    public event Action<DialogueNode> NodeBegan;

    /// <summary>对话结束、面板隐藏后（参数：对话资产）。</summary>
    public event Action<DialogueAsset> DialogueEnded;

    private DialogueUI _ui;
    private DialogueUIConfig _config;
    private DialogueUIConfig _activeStyle; // 当前对话生效样式（asset 覆盖 > 全局），语速/颜色从这读
    private DialogueAsset _asset;
    private DialogueNode _current;
    private State _state = State.Idle;
    private float _visible;      // 累计已显示字符数（浮点，驱动 maxVisibleCharacters）
    private int _totalChars;
    private int _startFrame;     // 同帧输入守卫：点 Cube 启动的那一帧不能同时推进第一句
    private float _arrowTime;
    private bool _currentWasRead; // 入已读集合“前”的判定：入集合后再判恒为真，本句会被误加速

    // ---- 会话记忆（跨对话保留；新游戏/测试用 ResetSession 清） ----
    private readonly DialogueVariables _variables = new DialogueVariables();
    private readonly List<(string speaker, string text)> _backlog = new List<(string, string)>();
    private readonly HashSet<string> _visitedKeys = new HashSet<string>(); // 键 = asset.name + "#" + node.Id
    private bool _autoPlay;
    private float _waitTimer;

    /// <summary>对话变量（选项条件/赋值、调试面板共用这一份）。</summary>
    public DialogueVariables Variables => _variables;

    /// <summary>已播句子（历史面板与存档共用）。</summary>
    public IReadOnlyList<(string speaker, string text)> Backlog => _backlog;

    /// <summary>获取或创建单例（触发 Awake 里的 UI 构建）。</summary>
    public static DialogueManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        var go = new GameObject("DialogueSystem");
        return go.AddComponent<DialogueManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _config = Resources.Load<DialogueUIConfig>(ConfigResourcePath);
        if (_config == null)
        {
            Debug.LogWarning("[Dialogue] 未找到 Resources/DialogueUIConfig，使用兜底样式。请运行菜单 Dialogue > Setup All。");
        }

        _activeStyle = _config;

        _ui = new DialogueUI();
        _ui.Build(transform, _config);
    }

    private void Update()
    {
        if (!IsPlaying)
        {
            return;
        }

        if (_state == State.Typing)
        {
            // 读 _activeStyle（每对话样式可覆盖语速）；读全局 _config 会让覆盖静默失效
            // 速度 = 基础 × 已读加速 × LeftControl 快进；两者都不触发时退化为原始语速（旧对话行为不变）
            float baseSpeed = _activeStyle != null ? _activeStyle.CharactersPerSecond : DefaultCharactersPerSecond;
            float speed = baseSpeed
                * (_currentWasRead && _activeStyle != null ? _activeStyle.ReadSpeedMultiplier : 1f)
                * (Input.GetKey(KeyCode.LeftControl) ? 10f : 1f);
            _visible += speed * Time.deltaTime;

            int shown = Mathf.Min(Mathf.FloorToInt(_visible), _totalChars);
            if (_ui.Body.maxVisibleCharacters != shown)
            {
                _ui.Body.maxVisibleCharacters = shown;
            }

            if (shown >= _totalChars)
            {
                CompleteTyping();
            }
        }

        _arrowTime += Time.deltaTime;
        _ui.Tick(_arrowTime);

        // 输入推进：打字中→立即补全；已显示完→下一句/结束。
        // 帧守卫：点 Cube 启动对话的那一帧，同一次点击不能又推进第一句。
        // Choosing 守卫：选项点击本身也走鼠标按下，不能同帧再被推进逻辑捕获；选项期间按空格也无效。
        bool pressed = _state != State.Choosing
            && Time.frameCount > _startFrame
            && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return));
        if (pressed)
        {
            Advance();
        }

        // A：自动播放开关（选项期间无效，避免选项页自己往下跑）
        if (Input.GetKeyDown(KeyCode.A) && _state != State.Choosing)
        {
            _autoPlay = !_autoPlay;
            _ui.SetAutoBadgeVisible(_autoPlay);
        }

        // H：历史面板开关
        if (Input.GetKeyDown(KeyCode.H))
        {
            if (_ui.IsHistoryVisible)
            {
                _ui.HideHistory();
            }
            else
            {
                _ui.ShowHistory(_backlog);
            }
        }

        // 自动播放：打完字才开始计时（CompleteTyping 归零）
        if (_state == State.WaitingAdvance && _autoPlay)
        {
            float delay = _activeStyle != null ? _activeStyle.AutoAdvanceDelay : 1.5f;
            _waitTimer += Time.deltaTime;
            if (_waitTimer >= delay)
            {
                Advance();
            }
        }
    }

    public void StartDialogue(DialogueAsset asset)
    {
        if (asset == null || IsPlaying)
        {
            return; // 幂等：播放中忽略新对话
        }

        var start = asset.GetStartNode();
        if (start == null)
        {
            Debug.LogWarning($"[Dialogue] 资产 {asset.name} 没有任何节点，跳过播放。");
            return;
        }

        PlayFrom(asset, start);
    }

    /// <summary>StartDialogue/RestoreSnapshot 共用播放入口。变量/backlog/已读刻意不清——跨对话会话记忆（任务链、已读加速都要），新游戏用 ResetSession 清。</summary>
    private void PlayFrom(DialogueAsset asset, DialogueNode node)
    {
        _asset = asset;
        IsPlaying = true;
        _startFrame = Time.frameCount;

        // 每对话样式覆盖：Show 之前就地重贴皮（无条件幂等，就地赋值极便宜）
        _activeStyle = asset.ResolveStyle(_config);
        _ui.ApplyStyle(_activeStyle);

        _ui.Show();
        DialogueStarted?.Invoke(_asset);
        EnterNode(node);
    }

    /// <summary>清空会话记忆（变量/backlog/已读），供新游戏或测试重置；不影响正在播的对话状态机。</summary>
    public void ResetSession()
    {
        _variables.Import(null); // Import(null) = 整体清空
        _backlog.Clear();
        _visitedKeys.Clear();
    }

    /// <summary>该节点是否已播过（已读加速/角标用）；键含资产名，跨资产不串。</summary>
    public bool IsNodeRead(DialogueAsset asset, DialogueNode node)
    {
        return asset != null && node != null && _visitedKeys.Contains(NodeKey(asset, node.Id));
    }

    private static string NodeKey(DialogueAsset asset, string nodeId)
    {
        return asset.name + "#" + nodeId;
    }

    private void EnterNode(DialogueNode node)
    {
        _current = node;
        NodeBegan?.Invoke(node);

        // 已读判定必须在入集合前取；随后入集合 + 追加历史
        string key = NodeKey(_asset, node.Id);
        _currentWasRead = _visitedKeys.Contains(key);
        _visitedKeys.Add(key);
        _backlog.Add((node.ResolvedSpeakerName, node.Text));

        _ui.SetSpeaker(node.Speaker, node.SpeakerName);
        _ui.SetBody(node.Text);
        _totalChars = _ui.Body.textInfo.characterCount; // SetBody 内部已 ForceMeshUpdate，此处为最新值
        _visible = 0f;
        _ui.SetContinueVisible(false);
        _state = State.Typing;
    }

    private void CompleteTyping()
    {
        _waitTimer = 0f; // 自动播放计时从打完字这一刻起算
        _visible = _totalChars;
        _ui.Body.maxVisibleCharacters = _totalChars;
        _ui.SetContinueVisible(true);
        _state = State.WaitingAdvance;
    }

    private void Advance()
    {
        if (_state == State.Typing)
        {
            CompleteTyping();
            return;
        }

        if (_state != State.WaitingAdvance)
        {
            return;
        }

        if (_current.HasChoices)
        {
            // 逐条算显示条件；语法错按“无条件显示”兜底并报错（编辑期 Validator 抓，运行时不卡死玩家）
            var choices = _current.Choices;
            var enabledFlags = new List<bool>(choices.Count);
            for (int i = 0; i < choices.Count; i++)
            {
                try
                {
                    enabledFlags.Add(DialogueExpression.Evaluate(_variables, choices[i].Condition));
                }
                catch (FormatException e)
                {
                    Debug.LogError($"[Dialogue] 选项条件解析失败（节点 {_current.Id}）：{e.Message}——按无条件显示。");
                    enabledFlags.Add(true);
                }
            }

            _ui.ShowChoices(choices, OnChoiceSelected, enabledFlags);
            _state = State.Choosing;
            return; // 有选项时 nextId 被忽略，去向由玩家点选决定
        }

        var next = _asset.GetNext(_current);
        if (next != null)
        {
            EnterNode(next);
        }
        else
        {
            EndDialogue();
        }
    }

    /// <summary>玩家点选选项：防重入 + 断链兜底（编辑期漏配不该静默卡死或静默结束）。</summary>
    private void OnChoiceSelected(int index)
    {
        if (_state != State.Choosing)
        {
            return;
        }

        var choice = _current.Choices[index];
        try
        {
            DialogueExpression.Apply(_variables, choice.SetExpressions); // 选中瞬间先结算副作用，再决定去向
        }
        catch (FormatException e)
        {
            Debug.LogError($"[Dialogue] 选项赋值执行失败（节点 {_current.Id}）：{e.Message}——跳过赋值继续。");
        }

        var nextId = choice.NextId;
        _ui.HideChoices();

        var next = string.IsNullOrEmpty(nextId) ? null : _asset.FindNode(nextId);
        if (next == null)
        {
            Debug.LogError($"[Dialogue] 选项 nextId \"{nextId}\" 无效（节点 {_current.Id}），强制结束对话。");
            EndDialogue();
            return;
        }

        EnterNode(next);
    }

    private void EndDialogue()
    {
        _state = State.Idle;
        IsPlaying = false;

        // 收掉会话期开关，下次开对话不带自动播放/历史面板残留
        _autoPlay = false;
        _ui.SetAutoBadgeVisible(false);
        if (_ui.IsHistoryVisible)
        {
            _ui.HideHistory();
        }

        _ui.Hide();

        var asset = _asset;
        _asset = null;
        _current = null;
        DialogueEnded?.Invoke(asset);
    }

    // ---- 快照/读档 ----

    /// <summary>当前进度快照（存档用）；仅播放中可取，否则返回 null。</summary>
    public DialogueSnapshot CaptureSnapshot()
    {
        if (!IsPlaying || _current == null || _asset == null)
        {
            return null;
        }

        var snap = new DialogueSnapshot
        {
            assetGuid = AssetKey(_asset),
            currentNodeId = _current.Id,
            visitedKeys = new List<string>(_visitedKeys),
            backlogSpeaker = new List<string>(_backlog.Count),
            backlogText = new List<string>(_backlog.Count)
        };
        foreach (var entry in _backlog)
        {
            snap.backlogSpeaker.Add(entry.speaker);
            snap.backlogText.Add(entry.text);
        }

        IReadOnlyDictionary<string, int> vars = _variables.GetAll();
        snap.varNames = new List<string>(vars.Keys);
        snap.varValues = new List<int>(vars.Values);
        return snap;
    }

    /// <summary>回放快照：校验归属 → 停当前对话 → 恢复会话记忆 → 走 StartDialogue 同样式路径进目标节点。
    /// EnterNode 会把当前句再追加一次，backlog 末尾与快照末句重复——可接受（省去特判）。</summary>
    public void RestoreSnapshot(DialogueSnapshot snap, DialogueAsset asset)
    {
        if (snap == null || asset == null)
        {
            Debug.LogWarning("[Dialogue] 读档失败：快照或资产为空。");
            return;
        }

        if (snap.assetGuid != AssetKey(asset))
        {
            Debug.LogError($"[Dialogue] 读档失败：快照归属不匹配（{snap.assetGuid} ≠ {AssetKey(asset)}）。");
            return;
        }

        if (IsPlaying)
        {
            EndDialogue(); // 换档前停当前对话（走 DialogueEnded，UI 归零）
        }

        _visitedKeys.Clear();
        if (snap.visitedKeys != null)
        {
            _visitedKeys.UnionWith(snap.visitedKeys);
        }

        _backlog.Clear();
        int backlogCount = snap.backlogSpeaker != null && snap.backlogText != null
            ? Mathf.Min(snap.backlogSpeaker.Count, snap.backlogText.Count)
            : 0;
        for (int i = 0; i < backlogCount; i++)
        {
            _backlog.Add((snap.backlogSpeaker[i], snap.backlogText[i]));
        }

        var vars = new Dictionary<string, int>();
        if (snap.varNames != null && snap.varValues != null)
        {
            int varCount = Mathf.Min(snap.varNames.Count, snap.varValues.Count);
            for (int i = 0; i < varCount; i++)
            {
                vars[snap.varNames[i]] = snap.varValues[i];
            }
        }
        _variables.Import(vars);

        var node = asset.FindNode(snap.currentNodeId);
        if (node == null)
        {
            Debug.LogError($"[Dialogue] 读档失败：节点 \"{snap.currentNodeId}\" 不在资产 {asset.name} 中。");
            return;
        }

        PlayFrom(asset, node);
    }

    /// <summary>资产标识：运行时无 AssetDatabase，只能用实例 ID；编辑器侧工具可自行换成 AssetDatabase 的 GUID。</summary>
    private static string AssetKey(DialogueAsset asset)
    {
        return asset.GetInstanceID().ToString();
    }
}

/// <summary>对话进度快照（纯数据）。assetGuid 实为资产实例 ID——运行时无 AssetDatabase，编辑器侧可换 GUID。</summary>
[Serializable]
public class DialogueSnapshot
{
    public string assetGuid;
    public string currentNodeId;
    public List<string> visitedKeys;
    public List<string> backlogSpeaker;
    public List<string> backlogText;
    public List<string> varNames;
    public List<int> varValues;
}
