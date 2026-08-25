using System;
using UnityEngine;

/// <summary>
/// 对话节点：一段说话内容 + 跳转。
/// nextId 留空 = 顺序播放下一个节点（末尾留空 = 结束）；
/// nextId 非空 = 跳转到指定 id 节点（为将来分支选项预留，加分支时零数据迁移）。
/// </summary>
[Serializable]
public class DialogueNode
{
    [SerializeField] private string id;

    [SerializeField] private SpeakerAsset speaker; // 角色资产引用（可空，空则用旧 speakerName 兼容）

    [SerializeField] private string speakerName; // 旧版纯文本说话人：speaker 为空时的回退，永不删除

    [SerializeField, TextArea(3, 8)] private string text;

    [SerializeField] private string nextId;

    public string Id => id;

    public SpeakerAsset Speaker => speaker;

    public string SpeakerName => speakerName;

    /// <summary>解析后的显示名：speaker 资产 &gt; 旧 speakerName &gt; 空串（旁白）。所有调用方统一读此属性。</summary>
    public string ResolvedSpeakerName => speaker != null ? speaker.GetDisplayName() : speakerName;

    public string Text => text;

    public string NextId => nextId;
}
