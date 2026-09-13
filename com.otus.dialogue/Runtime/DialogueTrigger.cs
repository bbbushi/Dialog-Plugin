using System.Collections;
using UnityEngine;

/// <summary>
/// 对话触发器：靠近+按键交互 / 自动播放两种启动方式（点击物体的 Demo 用法见 DialogueDemoTrigger）。
/// 靠近模式需要 Trigger Collider——编辑器加组件时自动补 Box Collider（Reset 兜底，运行时缺件报错）。
/// 靠近未播放时经 DialogueManager 显示「按 E 交谈」提示；对话资产为空/once 已触发都不会启动。
/// </summary>
[AddComponentMenu("Dialogue/Dialogue Trigger")]
public class DialogueTrigger : MonoBehaviour
{
    public enum TriggerMode
    {
        [InspectorName("靠近 + 按键")] InteractKey,
        [InspectorName("自动播放")] AutoStart,
    }

    [SerializeField] private DialogueAsset dialogue;
    [SerializeField] private TriggerMode mode = TriggerMode.InteractKey;

    [Header("靠近 + 按键")]
    [Tooltip("进入范围后按此键开始对话")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Tooltip("识别玩家的 Tag（Trigger Collider 只对该 Tag 进出生效）")]
    [SerializeField] private string playerTag = "Player";

    [Header("自动播放")]
    [Tooltip("场景加载后延迟多少秒开始")]
    [SerializeField] private float autoStartDelay;

    [Tooltip("勾选后本次运行只触发一次（运行期标记，不写入存档）")]
    [SerializeField] private bool once;

    private bool _fired;       // once 已触发标记（运行期，不序列化）
    private bool _inRange;     // 玩家当前在交互范围内
    private bool _promptShown; // 提示当前是否显示（状态变化才调 UI，防每帧重复）

    /// <summary>当前触发方式（管理器窗口的行内警示用）。</summary>
    public TriggerMode Mode => mode;

    private void Awake()
    {
        if (dialogue == null)
        {
            Debug.LogWarning($"[Dialogue] {name} 的 DialogueTrigger 未指定对话资产，不会触发。可在 Dialogue > Trigger Manager 里接线。", this);
        }

        if (mode == TriggerMode.InteractKey && !TryGetComponent<Collider>(out _))
        {
            Debug.LogError($"[Dialogue] {name} 用「靠近 + 按键」模式但物体上没有 Collider，交互不会生效。请加一个勾选 Is Trigger 的 Collider（如 Box Collider）。", this);
        }
    }

    private void Start()
    {
        if (mode == TriggerMode.AutoStart)
        {
            StartCoroutine(AutoStartRoutine());
        }
    }

    private IEnumerator AutoStartRoutine()
    {
        if (autoStartDelay > 0f)
        {
            yield return new WaitForSeconds(autoStartDelay);
        }

        if (dialogue != null && (!once || !_fired))
        {
            _fired = true;
            DialogueManager.EnsureInstance().StartDialogue(dialogue);
        }
    }

    private void Update()
    {
        if (mode != TriggerMode.InteractKey)
        {
            return;
        }

        bool canTalk = _inRange && dialogue != null
            && !DialogueManager.IsAnyPlaying
            && !(once && _fired);

        if (canTalk != _promptShown)
        {
            _promptShown = canTalk;
            if (canTalk)
            {
                DialogueManager.EnsureInstance().ShowInteractPrompt($"按 {interactKey} 交谈");
            }
            else if (DialogueManager.Instance != null)
            {
                DialogueManager.Instance.HideInteractPrompt();
            }
        }

        if (canTalk && Input.GetKeyDown(interactKey))
        {
            _fired = true;
            _promptShown = false;
            if (DialogueManager.Instance != null)
            {
                DialogueManager.Instance.HideInteractPrompt(); // 立即收提示（PlayFrom 也会收，双保险）
            }

            DialogueManager.EnsureInstance().StartDialogue(dialogue);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (mode == TriggerMode.InteractKey && !string.IsNullOrEmpty(playerTag) && other.CompareTag(playerTag))
        {
            _inRange = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (mode == TriggerMode.InteractKey && !string.IsNullOrEmpty(playerTag) && other.CompareTag(playerTag))
        {
            _inRange = false;
        }
    }

    private void OnDisable()
    {
        // 物体销毁/失活时 OnTriggerExit 不保证触发，这里收尾防提示残留；
        // 别的触发器若同帧也挂着提示，其 Update 下一帧会自动重新显示
        _inRange = false;
        if (_promptShown && DialogueManager.Instance != null)
        {
            _promptShown = false;
            DialogueManager.Instance.HideInteractPrompt();
        }
    }

    private void Reset()
    {
        // 编辑器首次添加组件：自动补 Trigger Collider，策划零配置
        if (GetComponent<Collider>() == null)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2f, 2f, 2f);
        }
    }
}
