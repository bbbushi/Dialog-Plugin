using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 布局契约：代码默认 UI 与「布局预制体」共用的节点名/必需组件清单。
/// 运行时 DialogueUI.Build 靠它按名解析引用并校验；编辑器「检查布局预制体」与
/// EditMode 测试复用同一份事实源，防默认实现与契约漂移。改任何节点名都要同步这里。
/// 必需节点缺失 → 回退代码默认布局并报错；可选节点缺失 → 对应功能静默降级。
/// ChoiceTemplate/EntryTemplate 为动态条目模板（导出工具附带）：应保持未激活，
/// 缺失或不可用时对应条目退回代码生成（出厂默认观感）。
/// </summary>
public static class DialogueUILayoutContract
{
    /// <summary>一个命名节点的契约：名字递归精确匹配 + 存在时必须挂的组件。</summary>
    public readonly struct NodeSpec
    {
        public readonly string Name;
        public readonly Type Component;   // null = 只要求 GameObject 存在
        public readonly bool Required;
        public readonly string MissingNote; // 可选节点缺失时的降级说明

        public NodeSpec(string name, Type component, bool required, string missingNote = null)
        {
            Name = name;
            Component = component;
            Required = required;
            MissingNote = missingNote ?? name;
        }
    }

    public static readonly NodeSpec[] Nodes =
    {
        // 必需：状态机每帧/每句都碰的功能件
        new NodeSpec("Panel", null, true),
        new NodeSpec("BodyText", typeof(TextMeshProUGUI), true),
        new NodeSpec("ChoiceRoot", null, true),
        new NodeSpec("HistoryRoot", null, true),
        new NodeSpec("HistoryScroll", typeof(ScrollRect), true),
        new NodeSpec("Viewport", null, true),
        new NodeSpec("Content", null, true),
        new NodeSpec("InteractPrompt", null, true),
        new NodeSpec("PromptText", typeof(TextMeshProUGUI), true),

        // 可选：删掉只降级对应表现，不影响播放
        new NodeSpec("PortraitFrame", typeof(Image), false, "不显示头像区"),
        new NodeSpec("PortraitImage", typeof(Image), false, "头像图不显示（走首字缩写需保留 PortraitInitial）"),
        new NodeSpec("PortraitInitial", typeof(TextMeshProUGUI), false, "无头像时不显示首字缩写"),
        new NodeSpec("NamePlate", typeof(Image), false, "不显示姓名牌"),
        new NodeSpec("NameText", typeof(TextMeshProUGUI), false, "不显示说话人名字"),
        new NodeSpec("ContinueArrow", typeof(TextMeshProUGUI), false, "不显示 ▼ 推进指示"),
        new NodeSpec("AutoBadge", typeof(TextMeshProUGUI), false, "不显示 AUTO 角标（自动播放仍可用）"),
        new NodeSpec("Title", typeof(TextMeshProUGUI), false, "历史窗无标题"),
        new NodeSpec("Hint", typeof(TextMeshProUGUI), false, "历史窗无关闭提示"),

        // 动态条目模板（应保持未激活）：存在时选项按钮/历史条目按模板克隆，配色字号归模板
        new NodeSpec("ChoiceTemplate", typeof(Button), false, "玩家选项按钮退回代码生成（出厂默认观感）"),
        new NodeSpec("EntryTemplate", typeof(TextMeshProUGUI), false, "历史条目退回代码生成"),
    };

    /// <summary>按名递归找第一个精确匹配的后代（不含根自身；层级深浅、嵌套位置自由）。</summary>
    public static Transform Find(Transform root, string name)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child.name == name)
            {
                return child;
            }

            var found = Find(child, name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// 校验层级：返回必需节点问题（空列表 = 可用）；optionalNotes 收集可选节点缺失的降级说明。
    /// </summary>
    public static List<string> Validate(GameObject root, List<string> optionalNotes = null)
    {
        var problems = new List<string>();
        if (root == null)
        {
            problems.Add("层级根为空");
            return problems;
        }

        foreach (var spec in Nodes)
        {
            var node = Find(root.transform, spec.Name);
            if (node == null)
            {
                if (spec.Required)
                {
                    problems.Add($"缺少必需节点「{spec.Name}」");
                }
                else
                {
                    optionalNotes?.Add($"无 {spec.Name}：{spec.MissingNote}");
                }

                continue;
            }

            if (spec.Component != null && node.GetComponent(spec.Component) == null)
            {
                if (spec.Required)
                {
                    problems.Add($"「{spec.Name}」缺少组件 {spec.Component.Name}");
                }
                else
                {
                    optionalNotes?.Add($"{spec.Name} 缺 {spec.Component.Name}：{spec.MissingNote}");
                }
            }
        }

        return problems;
    }
}
