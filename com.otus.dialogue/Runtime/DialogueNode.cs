using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 对话节点：一段说话内容 + 跳转/选项分支。
/// 流转优先级：choices 非空 → 玩家选项决定去向（nextId 被忽略）；
/// choices 为空：nextId 留空 = 顺序播放下一个节点（末尾留空 = 结束），nextId 非空 = 跳转。
/// </summary>
[Serializable]
public class DialogueNode
{
    [SerializeField] private string id;

    [SerializeField] private SpeakerAsset speaker; // 角色资产引用（可空，空则用旧 speakerName 兼容）

    [SerializeField] private string speakerName; // 旧版纯文本说话人：speaker 为空时的回退，永不删除

    [SerializeField, TextArea(3, 8)] private string text;

    [SerializeField] private string nextId;

    [SerializeField] private List<DialogueChoice> choices; // 玩家选项分支（空 = 无选项，保持线性/跳转）

    [SerializeField] private List<DialogueCommand> commands; // 进入命令槽（扩展点，2.0 起）：core 只派发不解释，语义归扩展包/项目脚本

    public string Id => id;

    public SpeakerAsset Speaker => speaker;

    public string SpeakerName => speakerName;

    /// <summary>解析后的显示名：speaker 资产 &gt; 旧 speakerName &gt; 空串（旁白）。所有调用方统一读此属性。</summary>
    public string ResolvedSpeakerName => speaker != null ? speaker.GetDisplayName() : speakerName;

    public string Text => text;

    public string NextId => nextId;

    public IReadOnlyList<DialogueChoice> Choices => choices;

    /// <summary>是否有玩家选项（运行时与预览窗共用此判定，防两处漂移）。</summary>
    public bool HasChoices => choices != null && choices.Count > 0;

    /// <summary>进入命令列表（可能为 null；调用方判空）。core 不解释语义。</summary>
    public IReadOnlyList<DialogueCommand> Commands => commands;
}

/// <summary>
/// 节点进入命令（扩展点，2.0 起）：core 只在进节点时派发（Manager.CommandReceived 事件），
/// 不解释语义——vn 扩展包解释 bgm=/sfx=，通用项目可解释成自己的演出指令或忽略。
/// 命令名建议小写字母（bgm / sfx / shake…），参数为自由文本。
/// </summary>
[Serializable]
public class DialogueCommand
{
    [SerializeField] private string name; // 命令名，如 bgm
    [SerializeField] private string value; // 参数，如 森林.mp3

    public string Name => name;

    public string Value => value;
}
