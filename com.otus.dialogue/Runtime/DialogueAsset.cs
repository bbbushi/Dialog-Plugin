using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 对话资产：节点列表容器。默认 Inspector 即可编辑。
/// 将来接入 LLM 数据源时，运行时 CreateInstance 组装同结构即可复用全部播放代码。
/// </summary>
[CreateAssetMenu(fileName = "NewDialogue", menuName = "Dialogue/Dialogue Asset")]
public class DialogueAsset : ScriptableObject
{
    /// <summary>节点图编辑器的节点画布位置（仅编辑器呈现用，运行时不读）。</summary>
    [Serializable]
    public class NodeLayout
    {
        [SerializeField] private string nodeId;
        [SerializeField] private float x;
        [SerializeField] private float y;

        public NodeLayout(string nodeId, float x, float y)
        {
            this.nodeId = nodeId;
            this.x = x;
            this.y = y;
        }

        public string NodeId => nodeId;
        public float X => x;
        public float Y => y;
    }

    [SerializeField] private List<DialogueNode> nodes = new List<DialogueNode>();

    [SerializeField] private DialogueUIConfig styleOverride; // 每对话样式覆盖（空 = 用全局默认）

    [SerializeField] private List<NodeLayout> layout = new List<NodeLayout>(); // 节点图坐标（id 对齐，幂等读写）

    public IReadOnlyList<DialogueNode> Nodes => nodes;

    public DialogueUIConfig StyleOverride => styleOverride;

    /// <summary>解析生效样式：本资产覆盖 &gt; 全局默认参数。</summary>
    public DialogueUIConfig ResolveStyle(DialogueUIConfig globalDefault)
    {
        return styleOverride != null ? styleOverride : globalDefault;
    }

    /// <summary>取节点图坐标；无记录时返回 null（编辑器用默认瀑布布局）。</summary>
    public NodeLayout GetLayout(string nodeId)
    {
        for (int i = 0; i < layout.Count; i++)
        {
            if (layout[i].NodeId == nodeId)
            {
                return layout[i];
            }
        }

        return null;
    }

    /// <summary>写节点图坐标（存在则覆盖，不存在则追加）；仅编辑器调用，配合 Undo.RecordObject。</summary>
    public void SetLayout(string nodeId, float x, float y)
    {
        for (int i = 0; i < layout.Count; i++)
        {
            if (layout[i].NodeId == nodeId)
            {
                layout[i] = new NodeLayout(nodeId, x, y);
                return;
            }
        }

        layout.Add(new NodeLayout(nodeId, x, y));
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
