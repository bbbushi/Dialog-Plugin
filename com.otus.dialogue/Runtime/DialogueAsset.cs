using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 对话资产：节点列表容器。默认 Inspector 即可编辑。
/// 将来接入 LLM 数据源时，运行时 CreateInstance 组装同结构即可复用全部播放代码。
/// </summary>
[CreateAssetMenu(fileName = "NewDialogue", menuName = "Dialogue/Dialogue Asset")]
public class DialogueAsset : ScriptableObject
{
    [SerializeField] private List<DialogueNode> nodes = new List<DialogueNode>();

    [SerializeField] private DialogueUIConfig styleOverride; // 每对话样式覆盖（空 = 用全局默认）

    public IReadOnlyList<DialogueNode> Nodes => nodes;

    public DialogueUIConfig StyleOverride => styleOverride;

    /// <summary>解析生效样式：本资产覆盖 &gt; 全局默认参数。</summary>
    public DialogueUIConfig ResolveStyle(DialogueUIConfig globalDefault)
    {
        return styleOverride != null ? styleOverride : globalDefault;
    }

    public DialogueNode GetStartNode()
    {
        return nodes.Count > 0 ? nodes[0] : null;
    }

    public DialogueNode FindNode(string id)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Id == id)
            {
                return nodes[i];
            }
        }

        Debug.LogWarning($"[Dialogue] 资产 {name} 中找不到节点 id=\"{id}\"");
        return null;
    }

    /// <summary>取当前节点的下一个节点；返回 null 表示对话结束。</summary>
    public DialogueNode GetNext(DialogueNode current)
    {
        if (current == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(current.NextId))
        {
            return FindNode(current.NextId);
        }

        int index = nodes.IndexOf(current);
        if (index < 0 || index + 1 >= nodes.Count)
        {
            return null; // 末尾节点且未指定跳转 = 结束
        }

        return nodes[index + 1];
    }
}
