using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Dialogue.Tests.EditMode
{
    /// <summary>
    /// DialogueValidator 校验规则的 EditMode 回归测试。
    /// 覆盖：顺序/结束语义不误报、重复 id、断链、空内容、孤岛、成环、空资产、空 id。
    /// </summary>
    public class DialogueValidatorTests
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

        private DialogueAsset CreateAsset(params (string id, string nextId, string speaker, string text)[] specs)
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
                node.FindPropertyRelative("speakerName").stringValue = specs[i].speaker;
                node.FindPropertyRelative("text").stringValue = specs[i].text;
                node.FindPropertyRelative("nextId").stringValue = specs[i].nextId;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        private static IEnumerable<DialogueIssue> IssuesOf(DialogueAsset asset, DialogueIssueType type)
        {
            return DialogueValidator.Validate(asset).Where(i => i.Type == type);
        }

        // ---------- 干净数据不误报 ----------

        [Test]
        public void Validate_AllLinearClean_NoIssues()
        {
            var asset = CreateAsset(
                ("n1", "", "旅人", "第一句。"),
                ("n2", "", "学者", "第二句。"),
                ("n3", "", "旁白", "第三句。"));

            Assert.That(DialogueValidator.Validate(asset), Is.Empty); // 顺序推进与末尾结束均为合法语义
        }

        [Test]
        public void Validate_ExplicitJumpChain_Clean()
        {
            // n1 显式跳到 n3，n3 顺序走到 n4，全部可达且无环
            var asset = CreateAsset(
                ("n1", "n3", "a", "一。"),
                ("n2", "", "b", "二。"),   // 被 n1 跳过 —— 但 n3 的顺序推进到 n4 而非 n2？不：n3 空走 n4（下一位）
                ("n3", "", "c", "三。"),
                ("n4", "", "d", "四。"));

            // n2 不可达（合法警告），其余干净
            var issues = DialogueValidator.Validate(asset);
            Assert.That(issues.Select(i => i.Type), Is.EqualTo(new[] { DialogueIssueType.UnreachableNode }));
            Assert.That(issues[0].NodeIndex, Is.EqualTo(1));
        }

        // ---------- 静态规则 ----------

        [Test]
        public void Validate_DuplicateId_ReportsSecondOccurrence()
        {
            var asset = CreateAsset(
                ("n1", "", "a", "一。"),
                ("n1", "", "b", "二。"));

            var dup = IssuesOf(asset, DialogueIssueType.DuplicateId).ToList();

            Assert.That(dup.Count, Is.EqualTo(1));
            Assert.That(dup[0].NodeIndex, Is.EqualTo(1));
            Assert.That(dup[0].Severity, Is.EqualTo(DialogueIssueSeverity.Error));
        }

        [Test]
        public void Validate_BrokenLink_IsError()
        {
            var asset = CreateAsset(("n1", "ghost", "a", "一。"), ("n2", "", "b", "二。"));

            var broken = IssuesOf(asset, DialogueIssueType.BrokenLink).ToList();

            Assert.That(broken.Count, Is.EqualTo(1));
            Assert.That(broken[0].Severity, Is.EqualTo(DialogueIssueSeverity.Error));
            Assert.That(broken[0].Message, Does.Contain("ghost"));
        }

        [Test]
        public void Validate_EmptyText_IsError_EmptySpeaker_IsWarning()
        {
            var asset = CreateAsset(
                ("n1", "", "", ""),   // 空 text（Error）+ 空 speaker（Warning）
                ("n2", "", "b", "二。"));

            Assert.That(IssuesOf(asset, DialogueIssueType.EmptyText).Single().Severity, Is.EqualTo(DialogueIssueSeverity.Error));
            Assert.That(IssuesOf(asset, DialogueIssueType.EmptySpeaker).Single().Severity, Is.EqualTo(DialogueIssueSeverity.Warning));
        }

        // ---------- 遍历规则 ----------

        [Test]
        public void Validate_Cycle_ReportsInfiniteLoopAndUnreachableTail()
        {
            // n1→n2→n1 成环；n3 可达吗？n1 走 nextId n2，环内无顺序路径到 n3 → n3 不可达
            var asset = CreateAsset(
                ("n1", "n2", "a", "一。"),
                ("n2", "n1", "b", "二。"),
                ("n3", "", "c", "三。"));

            var loop = IssuesOf(asset, DialogueIssueType.InfiniteLoop).ToList();
            var unreachable = IssuesOf(asset, DialogueIssueType.UnreachableNode).ToList();

            Assert.That(loop.Count, Is.EqualTo(1));
            Assert.That(loop[0].Severity, Is.EqualTo(DialogueIssueSeverity.Error));
            Assert.That(loop[0].Message, Does.Contain("n1 → n2 → n1"));
            Assert.That(unreachable.Single().NodeIndex, Is.EqualTo(2));
        }

        [Test]
        public void Validate_SelfJump_IsInfiniteLoop()
        {
            var asset = CreateAsset(("n1", "n1", "a", "一。"), ("n2", "", "b", "二。"));

            Assert.That(IssuesOf(asset, DialogueIssueType.InfiniteLoop).Count, Is.EqualTo(1));
        }

        // ---------- 资产级与边界 ----------

        [Test]
        public void Validate_EmptyAsset_ReportsNoNodes()
        {
            var asset = CreateAsset();

            var issue = DialogueValidator.Validate(asset).Single();

            Assert.That(issue.Type, Is.EqualTo(DialogueIssueType.NoNodes));
            Assert.That(issue.NodeIndex, Is.EqualTo(-1));
        }

        [Test]
        public void Validate_EmptyId_NoDuplicateFalsePositive()
        {
            var asset = CreateAsset(
                ("", "", "a", "一。"),
                ("", "", "b", "二。"));

            Assert.That(IssuesOf(asset, DialogueIssueType.DuplicateId), Is.Empty);
            Assert.That(IssuesOf(asset, DialogueIssueType.EmptyId).Count(), Is.EqualTo(2));
        }

        [Test]
        public void HasErrors_TrueOnlyWhenErrorPresent()
        {
            var clean = CreateAsset(("n1", "", "a", "一。"));
            var broken = CreateAsset(("n1", "ghost", "a", "一。"));

            Assert.That(DialogueValidator.HasErrors(DialogueValidator.Validate(clean)), Is.False);
            Assert.That(DialogueValidator.HasErrors(DialogueValidator.Validate(broken)), Is.True);
        }
    }
}
