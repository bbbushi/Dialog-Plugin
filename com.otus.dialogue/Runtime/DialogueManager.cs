using System;
using UnityEngine;

/// <summary>
/// 对话运行时驱动：状态机 + 打字机 + 输入推进。
/// 懒创建自举单例（场景零摆放），UI 由 DialogueUI 在运行时构建。
/// 打字机用 TMP 的 maxVisibleCharacters 递增：全文只设一次，布局只算一次，
/// 富文本标签不计入计数，将来加 <color> 等标记不破坏打字机。
/// </summary>
public class DialogueManager : MonoBehaviour
{
    /// <summary>UI 配置的 Resources 相对路径（预览窗口等编辑器工具复用，防路径漂移）。</summary>
    public const string ConfigResourcePath = "DialogueUIConfig";
    private const float DefaultCharactersPerSecond = 30f;

    private enum State { Idle, Typing, WaitingAdvance }

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
            float speed = _activeStyle != null ? _activeStyle.CharactersPerSecond : DefaultCharactersPerSecond;
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
        bool pressed = Time.frameCount > _startFrame
            && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return));
        if (pressed)
        {
            Advance();
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

        _asset = asset;
        IsPlaying = true;
        _startFrame = Time.frameCount;

        // 每对话样式覆盖：Show 之前就地重贴皮（无条件幂等，就地赋值极便宜）
        _activeStyle = asset.ResolveStyle(_config);
        _ui.ApplyStyle(_activeStyle);

        _ui.Show();
        DialogueStarted?.Invoke(_asset);
        EnterNode(start);
    }

    private void EnterNode(DialogueNode node)
    {
        _current = node;
        NodeBegan?.Invoke(node);

        _ui.SetSpeaker(node.Speaker, node.SpeakerName);
        _ui.SetBody(node.Text);
        _totalChars = _ui.Body.textInfo.characterCount; // SetBody 内部已 ForceMeshUpdate，此处为最新值
        _visible = 0f;
        _ui.SetContinueVisible(false);
        _state = State.Typing;
    }

    private void CompleteTyping()
    {
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

    private void EndDialogue()
    {
        _state = State.Idle;
        IsPlaying = false;
        _ui.Hide();

        var asset = _asset;
        _asset = null;
        _current = null;
        DialogueEnded?.Invoke(asset);
    }
}
