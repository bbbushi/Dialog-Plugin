using UnityEngine;

/// <summary>
/// Demo 触发器：点击挂载对象开始对话。
/// Awake 冻结 Rigidbody 防止对象掉落（可逆，不改场景结构）。
/// </summary>
public class DialogueDemoTrigger : MonoBehaviour
{
    [SerializeField] private DialogueAsset dialogue;

    private void Awake()
    {
        if (TryGetComponent<Rigidbody>(out var rb))
        {
            rb.isKinematic = true;
        }
    }

    private void OnMouseDown()
    {
        if (dialogue == null || DialogueManager.IsAnyPlaying)
        {
            return;
        }

        DialogueManager.EnsureInstance().StartDialogue(dialogue);
    }

    /// <summary>演示用快捷键：F5 快照存 1 号槽（播放中才可取），F9 从 1 号槽读档回放。</summary>
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5))
        {
            var snap = DialogueManager.Instance != null ? DialogueManager.Instance.CaptureSnapshot() : null;
            if (snap != null)
            {
                DialogueSaveSystem.Save(1, snap);
            }
        }

        if (Input.GetKeyDown(KeyCode.F9) && dialogue != null && DialogueSaveSystem.TryLoad(1, out var loaded))
        {
            DialogueManager.EnsureInstance().RestoreSnapshot(loaded, dialogue);
        }
    }
}
