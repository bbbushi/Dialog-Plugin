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
}
