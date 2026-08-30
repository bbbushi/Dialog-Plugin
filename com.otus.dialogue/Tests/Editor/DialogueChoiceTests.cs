using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Dialogue.Tests.EditMode
{
    /// <summary>
    /// 玩家选项分支的 EditMode 单元测试：HasChoices 判定、choices 读写、校验器三条选项规则、
    /// 以及 GetNext 不感知 choices 的数据层语义（防误改）。
    /// </summary>
    public class DialogueChoiceTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created)
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
            }

            created.Clear();
        }

        /// <summary>构造 DialogueAsset 并按 (id, nextId) 列表填充私有 nodes 字段（choices 留空 = 无选项）。</summary>
        private DialogueAsset CreateAsset(params (string id, string nextId)[] specs)
        {
            var asset = ScriptableObject.CreateInstance<DialogueAsset>();
            created.Add(asset);

            var so = new SerializedObject(asset);
            var nodes = so.FindProperty("nodes");
            nodes.arraySize = specs.Length;
            for (int i = 0; i < specs.Length; i++)
            {
                var node = nodes.GetArrayElementAtIndex(i);
                node.FindPropertyRelative("id").stringValue = specs[i].id;
                node.FindPropertyRelative("speakerName").stringValue = "说话人" + i;
                node.FindPropertyRelative("text").stringValue = "文本" + i;
                node.FindPropertyRelative("nextId").stringValue = specs[i].nextId;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        /// <summary>向指定节点追加一条选项（SerializedObject 写私有 choices 字段），返回同一资产。</summary>
        private static DialogueAsset AddChoice(DialogueAsset asset, string nodeId, string text, string nextId)
        {
            var so = new SerializedObject(asset);
            var nodes = so.FindProperty("nodes");
            for (int i = 0; i < nodes.arraySize; i++)
            {
                var node = nodes.GetArrayElementAtIndex(i);
                if (node.FindPropertyRelative("id").stringValue != nodeId)
                {
                    continue;
                }

                var choices = node.FindPropertyRelative("choices");
                choices.arraySize++;
                var choice = choices.GetArrayElementAtIndex(choices.arraySize - 1);
                choice.FindPropertyRelative("text").stringValue = text;
                choice.FindPropertyRelative("nextId").stringValue = nextId;
                so.ApplyModifiedPropertiesWithoutUndo();
                return asset;
            }

            Assert.Fail($"节点 {nodeId} 不存在");
            return asset;
        }

        private static IEnumerable<DialogueIssue> IssuesOf(DialogueAsset asset, DialogueIssueType type)
        {
            return DialogueValidator.Validate(asset).Where(i => i.Type == type);
        }

        // ---------- HasChoices ----------

        [Test]
        public void HasChoices_EmptyOrNull_ReturnsFalse()
        {
            var asset = CreateAsset(("n1", ""), ("n2", "")); // choices 字段未写：null 或空列表

            Assert.That(asset.FindNode("n1").HasChoices, Is.False);
        }

        [Test]
        public void HasChoices_NonEmpty_ReturnsTrue()
        {
            var asset = AddChoice(CreateAsset(("n1", ""), ("n2", "")), "n1", "去向 n2", "n2");

            Assert.That(asset.FindNode("n1").HasChoices, Is.True);
        }

        // ---------- Choices 读写回环 ----------

        [Test]
        public void Choices_ReadBack_MatchesSerialized()
        {
            var asset = AddChoice(CreateAsset(("n1", ""), ("n2", "")), "n1", "选项文案", "n2");

            var choices = asset.FindNode("n1").Choices;

            Assert.That(choices.Count, Is.EqualTo(1));
            Assert.That(choices[0].Text, Is.EqualTo("选项文案"));
            Assert.That(choices[0].NextId, Is.EqualTo("n2"));
        }

        // ---------- 校验规则：EmptyChoiceText ----------

        [Test]
        public void Validate_EmptyChoiceText_Warns()
        {
            var warned = AddChoice(CreateAsset(("n1", ""), ("n2", "")), "n1", "  ", "n2");
            var issue = IssuesOf(warned, DialogueIssueType.EmptyChoiceText).Single();

            Assert.That(issue.Severity, Is.EqualTo(DialogueIssueSeverity.Warning));
            Assert.That(issue.NodeIndex, Is.EqualTo(0));

            var clean = AddChoice(CreateAsset(("n1", ""), ("n2", "")), "n1", "有文案", "n2"); // 有文案不报

            Assert.That(IssuesOf(clean, DialogueIssueType.EmptyChoiceText), Is.Empty);
        }

        // ---------- 校验规则：BrokenChoiceLink ----------

        [Test]
        public void Validate_BrokenChoiceLink_Error()
        {
            // 变体一：nextId 为空
            var noTarget = AddChoice(CreateAsset(("n1", ""), ("n2", "")), "n1", "选项", "");
            var empty = IssuesOf(noTarget, DialogueIssueType.BrokenChoiceLink).Single();
            Assert.That(empty.Severity, Is.EqualTo(DialogueIssueSeverity.Error));
            Assert.That(empty.NodeIndex, Is.EqualTo(0));

            // 变体二：nextId 指向不存在的节点
            var ghost = AddChoice(CreateAsset(("n1", ""), ("n2", "")), "n1", "选项", "ghost");
            var broken = IssuesOf(ghost, DialogueIssueType.BrokenChoiceLink).Single();
            Assert.That(broken.Severity, Is.EqualTo(DialogueIssueSeverity.Error));
            Assert.That(broken.Message, Does.Contain("ghost"));
        }

        // ---------- 校验规则：NextIdIgnoredByChoices ----------

        [Test]
        public void Validate_NextIdIgnoredByChoices_Info()
        {
            var hasBoth = AddChoice(CreateAsset(("n1", "n2"), ("n2", "")), "n1", "分支", "n2");
            var issue = IssuesOf(hasBoth, DialogueIssueType.NextIdIgnoredByChoices).Single();

            Assert.That(issue.Severity, Is.EqualTo(DialogueIssueSeverity.Info));
            Assert.That(issue.NodeIndex, Is.EqualTo(0));

            var noNextId = AddChoice(CreateAsset(("n1", ""), ("n2", "")), "n1", "分支", "n2"); // nextId 留空不报

            Assert.That(IssuesOf(noNextId, DialogueIssueType.NextIdIgnoredByChoices), Is.Empty);
        }

        // ---------- 数据层语义：GetNext 不感知 choices ----------

        [Test]
        public void GetNext_WithChoices_StillUsesNextId()
        {
            // 文档化：GetNext 只认 nextId/顺序，选项走向由运行时与预览窗自行处理
            var asset = AddChoice(CreateAsset(("n1", "n2"), ("n2", ""), ("n3", "")), "n1", "去 n3", "n3");

            var next = asset.GetNext(asset.FindNode("n1"));

            Assert.That(next.Id, Is.EqualTo("n2"));
        }
    }
}
