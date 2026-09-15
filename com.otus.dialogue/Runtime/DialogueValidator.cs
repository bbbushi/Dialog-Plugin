using System;
using System.Collections.Generic;
using System.Text;

/// <summary>校验问题严重级别。</summary>
public enum DialogueIssueSeverity
{
    Error,
    Warning,

    /// <summary>合法但易误解的数据，仅提示。</summary>
    Info,
}

/// <summary>校验问题类型。</summary>
public enum DialogueIssueType
{
    /// <summary>资产没有任何节点（警告级）。</summary>
    NoNodes,

    /// <summary>节点 id 为空（警告级：破坏下拉展示与可引用性）。</summary>
    EmptyId,

    /// <summary>重复 id（错误级）。</summary>
    DuplicateId,

    /// <summary>nextId 指向不存在的节点——断链（错误级）。</summary>
    BrokenLink,

    /// <summary>正文为空（错误级：播放时闪现空面板）。</summary>
    EmptyText,

    /// <summary>说话人为空（警告级：合法风格，运行时姓名牌自动隐藏）。</summary>
    EmptySpeaker,

    /// <summary>从起始节点遍历不到——孤岛（警告级）。</summary>
    UnreachableNode,

    /// <summary>nextId 成环，对话永不结束（错误级）。</summary>
    InfiniteLoop,

    /// <summary>选项文案为空（警告级：按钮上无可读文本）。</summary>
    EmptyChoiceText,

    /// <summary>选项 nextId 为空或指向不存在的节点——选项断链（错误级）。</summary>
    BrokenChoiceLink,

    /// <summary>choices 非空时节点 nextId 被忽略（提示级：去向由玩家选择决定）。</summary>
    NextIdIgnoredByChoices,

    /// <summary>选项条件/赋值表达式语法错误（错误级：运行时按无条件/跳过赋值兜底并报错）。</summary>
    BadChoiceCondition,

    /// <summary>进入命令名为空（警告级：派发时订阅方无从解释。core 不校验命令语义）。</summary>
    BadCommandName,
}

/// <summary>一条校验结果。NodeIndex = -1 表示资产级问题。</summary>
public sealed class DialogueIssue
{
    public DialogueIssue(DialogueIssueType type, DialogueIssueSeverity severity, int nodeIndex, string nodeId, string message)
    {
        Type = type;
        Severity = severity;
        NodeIndex = nodeIndex;
        NodeId = nodeId;
        Message = message;
    }

    public DialogueIssueType Type { get; }

    public DialogueIssueSeverity Severity { get; }

    public int NodeIndex { get; }

    public string NodeId { get; }

    public string Message { get; }
}

/// <summary>
/// 对话资产静态校验器：断链 / 重复 id / 空内容 / 选项 / 不可达 / 死循环。
/// 纯 C#、静默（不写 Console）——供 Inspector 徽标、保存钩子、测试与未来的 LLM 运行时校验共用。
/// </summary>
public static class DialogueValidator
{
    public static List<DialogueIssue> Validate(DialogueAsset asset)
    {
        var issues = new List<DialogueIssue>();
        if (asset == null)
        {
            return issues;
        }

        var nodes = asset.Nodes;

        if (nodes.Count == 0)
        {
            issues.Add(new DialogueIssue(
                DialogueIssueType.NoNodes, DialogueIssueSeverity.Warning, -1, null,
                "对话资产没有任何节点。"));
            return issues;
        }

        // ---- 逐节点静态检查 + 建 id 索引（自建字典，绝不调 asset.FindNode——那会打警告）----
        var indexById = new Dictionary<string, int>();
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            if (string.IsNullOrEmpty(node.Text))
            {
                issues.Add(Issue(DialogueIssueType.EmptyText, DialogueIssueSeverity.Error, i, node, "正文为空，播放时会闪现空面板。"));
            }

            if (string.IsNullOrEmpty(node.ResolvedSpeakerName)) // 解析链：speaker 资产 > 旧 speakerName
            {
                issues.Add(Issue(DialogueIssueType.EmptySpeaker, DialogueIssueSeverity.Warning, i, node, "说话人为空（运行时姓名牌将隐藏，若非旁白请补上）。"));
            }

            // 进入命令：只查空名，不校验语义（语义归扩展包/项目脚本定义）
            var commands = node.Commands;
            if (commands != null)
            {
                for (int c = 0; c < commands.Count; c++)
                {
                    if (string.IsNullOrWhiteSpace(commands[c].Name))
                    {
                        issues.Add(Issue(DialogueIssueType.BadCommandName, DialogueIssueSeverity.Warning, i, node,
                            $"进入命令 #{c + 1} 名字为空，订阅方无从解释。"));
                    }
                }
            }

            string id = node.Id;
            if (string.IsNullOrEmpty(id))
            {
                issues.Add(Issue(DialogueIssueType.EmptyId, DialogueIssueSeverity.Warning, i, node, "节点 id 为空，无法被跳转引用。"));
                continue; // 空 id 不参与重复检测，防止两个空 id 互报
            }

            if (indexById.TryGetValue(id, out int firstIndex))
            {
                issues.Add(Issue(DialogueIssueType.DuplicateId, DialogueIssueSeverity.Error, i, node,
                    $"id \"{id}\" 与节点 #{firstIndex + 1} 重复，跳转目标会有二义性。"));
            }
            else
            {
                indexById[id] = i;
            }
        }

        // ---- 选项检查（须在 id 索引建完后：选项可前向引用后面的节点）----
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            if (node.HasChoices && !string.IsNullOrEmpty(node.NextId))
            {
                issues.Add(Issue(DialogueIssueType.NextIdIgnoredByChoices, DialogueIssueSeverity.Info, i, node,
                    "节点 nextId 将被选项忽略，去向由玩家选择决定。"));
            }

            var choices = node.Choices;
            if (choices == null)
            {
                continue;
            }

            for (int c = 0; c < choices.Count; c++)
            {
                var choice = choices[c];
                if (choice == null)
                {
                    continue; // 序列化列表不该出现，防御运行时拼装数据
                }

                if (string.IsNullOrWhiteSpace(choice.Text))
                {
                    issues.Add(Issue(DialogueIssueType.EmptyChoiceText, DialogueIssueSeverity.Warning, i, node,
                        $"选项 #{c + 1} 文案为空。"));
                }

                string choiceNextId = choice.NextId;
                if (string.IsNullOrEmpty(choiceNextId))
                {
                    issues.Add(Issue(DialogueIssueType.BrokenChoiceLink, DialogueIssueSeverity.Error, i, node,
                        $"选项 #{c + 1} 未设置跳转目标（断链）。"));
                }
                else if (!indexById.ContainsKey(choiceNextId)) // 查自建字典，绝不调 FindNode（会打警告）
                {
                    issues.Add(Issue(DialogueIssueType.BrokenChoiceLink, DialogueIssueSeverity.Error, i, node,
                        $"选项 #{c + 1} 跳转目标 \"{choiceNextId}\" 不存在（断链）。"));
                }

                // 条件/赋值语法：临时变量实例试执行（未定义变量按 0，动态语义不校验存在性）
                var probe = new DialogueVariables();
                if (!string.IsNullOrWhiteSpace(choice.Condition))
                {
                    try
                    {
                        DialogueExpression.Evaluate(probe, choice.Condition);
                    }
                    catch (FormatException e)
                    {
                        issues.Add(Issue(DialogueIssueType.BadChoiceCondition, DialogueIssueSeverity.Error, i, node,
                            $"选项 #{c + 1} 条件语法错误：{e.Message}"));
                    }
                }

                if (!string.IsNullOrWhiteSpace(choice.SetExpressions))
                {
                    try
                    {
                        DialogueExpression.Apply(probe, choice.SetExpressions);
                    }
                    catch (FormatException e)
                    {
                        issues.Add(Issue(DialogueIssueType.BadChoiceCondition, DialogueIssueSeverity.Error, i, node,
                            $"选项 #{c + 1} 赋值语法错误：{e.Message}"));
                    }
                }
            }
        }

        // ---- 从起始节点走链（单后继模型：nextId 非空走字典、空走 i+1）----
        var visited = new HashSet<int>();
        var chain = new List<int>();
        int current = 0;
        while (current >= 0 && current < nodes.Count)
        {
            if (visited.Contains(current))
            {
                // 单后继模型下成环即死循环（出边唯一，指向环内就无法同时指向环外）
                issues.Add(Issue(DialogueIssueType.InfiniteLoop, DialogueIssueSeverity.Error, current, nodes[current],
                    "跳转成环，对话永远不会结束：" + DescribeCycle(chain, current, nodes)));
                break;
            }

            visited.Add(current);
            chain.Add(current);

            string nextId = nodes[current].NextId;
            if (string.IsNullOrEmpty(nextId))
            {
                current = current + 1; // 顺序推进（末尾节点越界即自然结束）
                continue;
            }

            if (indexById.TryGetValue(nextId, out int target))
            {
                current = target;
            }
            else
            {
                issues.Add(Issue(DialogueIssueType.BrokenLink, DialogueIssueSeverity.Error, current, nodes[current],
                    $"跳转目标 \"{nextId}\" 不存在（断链）。"));
                break; // 链在此断开，后续节点交由不可达检测覆盖
            }
        }

        // ---- 不可达：visited 的补集 ----
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!visited.Contains(i))
            {
                issues.Add(Issue(DialogueIssueType.UnreachableNode, DialogueIssueSeverity.Warning, i, nodes[i],
                    "从起始节点走不到此节点（孤岛），永远不会被播放。"));
            }
        }

        return issues;
    }

    public static bool HasErrors(IReadOnlyList<DialogueIssue> issues)
    {
        for (int i = 0; i < issues.Count; i++)
        {
            if (issues[i].Severity == DialogueIssueSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>环段描述：n2 → n3 → n2。</summary>
    private static string DescribeCycle(List<int> chain, int repeatIndex, IReadOnlyList<DialogueNode> nodes)
    {
        int start = chain.IndexOf(repeatIndex);
        var sb = new StringBuilder();
        for (int i = start; i < chain.Count; i++)
        {
            if (i > start)
            {
                sb.Append(" → ");
            }

            sb.Append(DescribeNode(chain[i], nodes));
        }

        sb.Append(" → ").Append(DescribeNode(repeatIndex, nodes));
        return sb.ToString();
    }

    private static string DescribeNode(int index, IReadOnlyList<DialogueNode> nodes)
    {
        string id = nodes[index].Id;
        return string.IsNullOrEmpty(id) ? $"#{index + 1}" : id;
    }

    private static DialogueIssue Issue(DialogueIssueType type, DialogueIssueSeverity severity, int index, DialogueNode node, string message)
    {
        string prefix = $"[节点 #{index + 1}{(node != null && !string.IsNullOrEmpty(node.Id) ? $" id=\"{node.Id}\"" : "")}] ";
        return new DialogueIssue(type, severity, index, node != null ? node.Id : null, prefix + message);
    }
}
