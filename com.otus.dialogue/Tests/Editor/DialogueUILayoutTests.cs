using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Dialogue.Tests.EditMode
{
    /// <summary>
    /// UI 布局契约测试：代码默认层级必须满足契约（防默认实现与契约漂移）、
    /// 校验对缺必需节点/缺组件/缺可选节点的判定、按名递归查找、坏预制体回退。
    /// </summary>
    public class DialogueUILayoutTests
    {
        private GameObject _root;
        private DialogueUI _ui;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("LayoutTestRoot");
            _ui = new DialogueUI();
            _ui.Build(_root.transform, null); // null cfg → 无布局预制体，走代码默认布局
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root); // DialogueCanvas（含自举 EventSystem）随之销毁
            }
        }

        private GameObject Canvas => _root.transform.GetChild(0).gameObject;

        // ---------- 默认实现 ↔ 契约不漂移 ----------

        [Test]
        public void Build_DefaultHierarchy_SatisfiesRequiredContract()
        {
            var problems = DialogueUILayoutContract.Validate(Canvas);
            Assert.AreEqual(0, problems.Count, "代码默认布局必须满足契约（改了节点名要同步契约）：\n" + string.Join("\n", problems));
        }

        [Test]
        public void Find_LocatesDeeplyNestedNode()
        {
            var promptText = DialogueUILayoutContract.Find(Canvas.transform, "PromptText");
            Assert.IsNotNull(promptText, "递归查找应命中深层节点");
            Assert.AreEqual("InteractPrompt", promptText.parent.name, "PromptText 应在 InteractPrompt 之下");
        }

        // ---------- 校验判定 ----------

        [Test]
        public void Validate_MissingRequiredNode_IsReported()
        {
            Object.DestroyImmediate(DialogueUILayoutContract.Find(Canvas.transform, "BodyText").gameObject);
            var problems = DialogueUILayoutContract.Validate(Canvas);
            Assert.AreEqual(1, problems.Count, string.Join("\n", problems));
            StringAssert.Contains("BodyText", problems[0]);
        }

        [Test]
        public void Validate_MissingOptionalNode_PassesWithNote()
        {
            // 可选件整块删掉：姓名牌（含其子 NameText）
            Object.DestroyImmediate(DialogueUILayoutContract.Find(Canvas.transform, "NamePlate").gameObject);
            var notes = new List<string>();
            var problems = DialogueUILayoutContract.Validate(Canvas, notes);
            Assert.AreEqual(0, problems.Count, "可选节点缺失不应判为失败：\n" + string.Join("\n", problems));
            Assert.GreaterOrEqual(notes.Count, 2, "NamePlate/NameText 缺失应记入降级说明");
        }

        [Test]
        public void Validate_OptionalNodeWithoutComponent_IsNoteNotProblem()
        {
            Object.DestroyImmediate(DialogueUILayoutContract.Find(Canvas.transform, "NamePlate").GetComponent<Image>());
            var notes = new List<string>();
            var problems = DialogueUILayoutContract.Validate(Canvas, notes);
            Assert.AreEqual(0, problems.Count, "缺组件的可选节点按降级处理，不判失败");
            StringAssert.Contains("NamePlate", string.Join("\n", notes));
        }

        // ---------- 坏预制体回退 ----------

        [Test]
        public void Build_BrokenLayoutPrefab_LogsErrorAndFallsBackToCodeLayout()
        {
            LogAssert.Expect(LogType.Error, new Regex("布局预制体缺必需节点"));

            var broken = new GameObject("BrokenPrefab"); // 空层级：缺所有必需节点
            var cfg = ScriptableObject.CreateInstance<DialogueUIConfig>();
            var host = new GameObject("FallbackHost"); // 独立宿主：_root 已有 SetUp 建的画布
            try
            {
                var so = new SerializedObject(cfg);
                so.FindProperty("layoutPrefab").objectReferenceValue = broken;
                so.ApplyModifiedPropertiesWithoutUndo();

                var ui = new DialogueUI();
                ui.Build(host.transform, cfg);

                // 坏实例已清理 + 回退成代码默认：宿主下恰一个 DialogueCanvas 且契约齐全
                Assert.AreEqual(1, host.transform.childCount, "坏预制体实例应被清理，不得残留");
                Assert.AreEqual("DialogueCanvas", host.transform.GetChild(0).name);
                Assert.AreEqual(0, DialogueUILayoutContract.Validate(host.transform.GetChild(0).gameObject).Count);
            }
            finally
            {
                Object.DestroyImmediate(broken);
                Object.DestroyImmediate(cfg);
                Object.DestroyImmediate(host);
            }
        }
    }
}
