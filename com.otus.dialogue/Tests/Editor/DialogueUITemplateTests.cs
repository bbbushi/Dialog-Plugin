using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Dialogue.Tests.EditMode
{
    /// <summary>
    /// 动态条目模板测试（1.5.0）：出厂模板满足契约、选项克隆（回调/命名/保模板隐藏/置灰保色相）、
    /// 无模板或坏模板降级回代码生成、历史条目保色与宽度封顶、清空不误删模板、
    /// ApplyStyle 对模板条目只刷字体不刷色。技巧：场景画布直接当 layoutPrefab 源
    /// （Instantiate 场景对象等价实例化预制体，免落盘，同 DialogueUILayoutTests 的坏预制体测试）。
    /// </summary>
    public class DialogueUITemplateTests
    {
        private GameObject _root;         // 模板源根（代码布局 + 出厂模板）
        private GameObject _sourceCanvas; // 源画布本体
        private GameObject _host;         // 被测 UI 宿主
        private DialogueUIConfig _cfg;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TemplateSourceRoot");
            var source = new DialogueUI();
            source.Build(_root.transform, null);
            _sourceCanvas = _root.transform.GetChild(0).gameObject;
            DialogueUI.AppendDefaultTemplates(_sourceCanvas.transform);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }

            if (_cfg != null)
            {
                Object.DestroyImmediate(_cfg);
            }
        }

        /// <summary>以模板源画布为 layoutPrefab 构建被测 UI（每测试一次；宿主与配置由 TearDown 统一销毁）。</summary>
        private DialogueUI BuildUi()
        {
            _host = new GameObject("TemplateHost");
            _cfg = ScriptableObject.CreateInstance<DialogueUIConfig>();
            var so = new SerializedObject(_cfg);
            so.FindProperty("layoutPrefab").objectReferenceValue = _sourceCanvas;
            so.ApplyModifiedPropertiesWithoutUndo();

            var ui = new DialogueUI();
            ui.Build(_host.transform, _cfg);
            return ui;
        }

        private static DialogueChoice ChoiceOf(string text)
        {
            return JsonUtility.FromJson<DialogueChoice>($"{{\"text\":\"{text}\"}}");
        }

        private static TextMeshProUGUI Text(Transform root, string name)
        {
            return DialogueUILayoutContract.Find(root, name).GetComponent<TextMeshProUGUI>();
        }

        // ---------- 出厂模板 ----------

        [Test]
        public void AppendDefaultTemplates_CreatesContractCompliantInactiveTemplates()
        {
            var notes = new List<string>();
            Assert.AreEqual(0, DialogueUILayoutContract.Validate(_sourceCanvas, notes).Count, "模板就位后仍须满足契约");
            Assert.AreEqual(0, notes.Count, "模板就位后不应再有模板降级说明");

            var choice = DialogueUILayoutContract.Find(_sourceCanvas.transform, "ChoiceTemplate");
            Assert.IsFalse(choice.gameObject.activeSelf, "选项模板必须未激活");
            Assert.IsNotNull(choice.GetComponent<Button>(), "契约组件：Button");
            Assert.IsNotNull(DialogueUILayoutContract.Find(choice, "Label").GetComponent<TextMeshProUGUI>(), "文本子节点 Label");
            Assert.AreEqual("ChoiceRoot", choice.parent.name, "工厂默认挂在直觉位置");

            var entry = DialogueUILayoutContract.Find(_sourceCanvas.transform, "EntryTemplate");
            Assert.IsFalse(entry.gameObject.activeSelf, "历史模板必须未激活");
            Assert.IsNotNull(entry.GetComponent<TextMeshProUGUI>(), "根即 TMP 文本");
            Assert.AreEqual("Content", entry.parent.name);
        }

        // ---------- 选项克隆 ----------

        [Test]
        public void ShowChoices_TemplateCloned_BindsCallbacks_KeepsTemplateHidden()
        {
            var ui = BuildUi();
            int picked = -1;
            ui.ShowChoices(new[] { ChoiceOf("甲"), ChoiceOf("乙") }, i => picked = i);

            var choiceRoot = DialogueUILayoutContract.Find(_host.transform, "ChoiceRoot");
            Assert.AreEqual(3, choiceRoot.childCount, "两个克隆 + 一个未激活模板");

            var clone = choiceRoot.Find("Choice_1");
            Assert.IsTrue(clone.gameObject.activeSelf, "克隆必须显式激活（源模板未激活）");
            Assert.AreEqual(40f, clone.GetComponent<LayoutElement>().minHeight, "尺寸数值随模板");
            Assert.AreEqual("乙", Text(clone, "Label").text);

            clone.GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(1, picked, "回调索引正确");

            var template = choiceRoot.Find("ChoiceTemplate");
            Assert.IsNotNull(template, "模板本体在填充后存活");
            Assert.IsFalse(template.gameObject.activeSelf, "模板本体保持未激活");
        }

        [Test]
        public void ShowChoices_TemplateWithoutLabel_DegradesToCodeGeneration()
        {
            Object.DestroyImmediate(DialogueUILayoutContract.Find(_sourceCanvas.transform, "Label").gameObject);
            LogAssert.Expect(LogType.Warning, new Regex("ChoiceTemplate"));
            var ui = BuildUi();

            var so = new SerializedObject(_cfg);
            so.FindProperty("choiceColor").colorValue = Color.magenta;
            so.FindProperty("choiceTextColor").colorValue = Color.cyan;
            so.ApplyModifiedPropertiesWithoutUndo();

            ui.ShowChoices(new[] { ChoiceOf("甲") }, i => { });

            var clone = DialogueUILayoutContract.Find(_host.transform, "Choice_0");
            Assert.IsNotNull(clone, "无可用模板时回退代码生成");
            Assert.AreEqual(Color.magenta, clone.GetComponent<Image>().color, "降级分支配色归配置");
            var label = Text(clone, "Label");
            Assert.AreEqual(24, label.fontSize);
            Assert.AreEqual(Color.cyan, label.color);
        }

        [Test]
        public void ShowChoices_NoTemplate_KeepsExistingCodeBranch()
        {
            Object.DestroyImmediate(DialogueUILayoutContract.Find(_sourceCanvas.transform, "ChoiceTemplate").gameObject);
            Object.DestroyImmediate(DialogueUILayoutContract.Find(_sourceCanvas.transform, "EntryTemplate").gameObject);
            LogAssert.Expect(LogType.Log, new Regex("降级模式"));
            var ui = BuildUi();

            int picked = -1;
            ui.ShowChoices(new[] { ChoiceOf("甲") }, i => picked = i);

            var clone = DialogueUILayoutContract.Find(_host.transform, "Choice_0");
            var element = clone.GetComponent<LayoutElement>();
            Assert.AreEqual(40f, element.minHeight);
            Assert.AreEqual(360f, element.preferredWidth);
            Assert.IsNotNull(clone.GetComponent<Button>());

            clone.GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(0, picked);
        }

        [Test]
        public void ShowChoices_DisabledTemplateClone_DimsAlphaAndSkipsCallback()
        {
            var ui = BuildUi();
            int picked = -1;
            ui.ShowChoices(new[] { ChoiceOf("甲"), ChoiceOf("乙") }, i => picked = i, new[] { true, false });

            var disabled = DialogueUILayoutContract.Find(_host.transform, "Choice_1");
            // 出厂模板底色 (0.96,0.91,0.82,0.95) → 置灰只压 alpha ×0.45，色相保留
            Assert.AreEqual(0.95f * 0.45f, disabled.GetComponent<Image>().color.a, 0.001f);
            Assert.AreEqual(0.96f, disabled.GetComponent<Image>().color.r, 0.01f, "压透明度不改色相，兼容模板配色");

            disabled.GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(-1, picked, "置灰项不挂回调");

            DialogueUILayoutContract.Find(_host.transform, "Choice_0").GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(0, picked, "可用项回调正常");
        }

        // ---------- 历史条目克隆 ----------

        [Test]
        public void ShowHistory_TemplateCloned_KeepsTemplateColorAndCapsWidth()
        {
            Text(_sourceCanvas.transform, "EntryTemplate").color = Color.red;
            var ui = BuildUi();

            var so = new SerializedObject(_cfg);
            so.FindProperty("textColor").colorValue = Color.blue;
            so.ApplyModifiedPropertiesWithoutUndo();

            ui.ShowHistory(new[] { ("学者", "你好"), ("", "旁白") });

            var entry = DialogueUILayoutContract.Find(_host.transform, "Entry_1");
            Assert.IsTrue(entry.gameObject.activeSelf, "克隆必须显式激活（源模板未激活）");
            Assert.AreEqual("旁白", entry.GetComponent<TextMeshProUGUI>().text, "无 speaker 只出正文");
            Assert.AreEqual(Color.red, entry.GetComponent<TextMeshProUGUI>().color, "模板条目配色归模板，不套配置色");

            // 出厂模板 preferredWidth 留空 → 克隆期按「面板宽-80」封顶
            float expected = Mathf.Max(100f, ((RectTransform)DialogueUILayoutContract.Find(_host.transform, "Panel")).rect.width - 80f);
            Assert.AreEqual(expected, entry.GetComponent<LayoutElement>().preferredWidth, 0.01f);
        }

        [Test]
        public void ShowHistory_TemplateWithOwnWidth_KeepsTemplateWidth()
        {
            var template = DialogueUILayoutContract.Find(_sourceCanvas.transform, "EntryTemplate");
            template.GetComponent<LayoutElement>().preferredWidth = 500f; // 策划明确给了宽度
            var ui = BuildUi();

            ui.ShowHistory(new[] { ("", "文本") });

            Assert.AreEqual(500f,
                DialogueUILayoutContract.Find(_host.transform, "Entry_0").GetComponent<LayoutElement>().preferredWidth,
                0.01f, "模板有值则代码不覆盖");
        }

        [Test]
        public void ShowHistory_NoTemplate_KeepsCodeGeneration()
        {
            Object.DestroyImmediate(DialogueUILayoutContract.Find(_sourceCanvas.transform, "ChoiceTemplate").gameObject);
            Object.DestroyImmediate(DialogueUILayoutContract.Find(_sourceCanvas.transform, "EntryTemplate").gameObject);
            LogAssert.Expect(LogType.Log, new Regex("降级模式"));
            var ui = BuildUi();

            var so = new SerializedObject(_cfg);
            so.FindProperty("textColor").colorValue = Color.blue;
            so.ApplyModifiedPropertiesWithoutUndo();

            ui.ShowHistory(new[] { ("", "旁白文本") });

            var entry = Text(_host.transform, "Entry_0");
            Assert.AreEqual(22, entry.fontSize);
            Assert.AreEqual(Color.blue, entry.color, "无模板分支配色归配置");
        }

        // ---------- 模板存活与换肤 ----------

        [Test]
        public void RepeatedShowChoices_TemplateSurvivesClearing()
        {
            var ui = BuildUi();
            ui.ShowChoices(new[] { ChoiceOf("甲") }, i => { });
            ui.HideChoices();
            ui.ShowChoices(new[] { ChoiceOf("乙"), ChoiceOf("丙") }, i => { });
            ui.HideChoices();

            var choiceRoot = DialogueUILayoutContract.Find(_host.transform, "ChoiceRoot");
            Assert.AreEqual(1, choiceRoot.childCount, "清空后只剩模板本体（EditMode 销毁确定性）");
            Assert.AreEqual("ChoiceTemplate", choiceRoot.GetChild(0).name);
            Assert.IsFalse(choiceRoot.GetChild(0).gameObject.activeSelf);

            ui.ShowChoices(new[] { ChoiceOf("丁") }, i => { }); // 模板未丢，仍走克隆分支
            Assert.AreEqual(2, choiceRoot.childCount);
            Assert.AreEqual("丁", Text(DialogueUILayoutContract.Find(choiceRoot, "Choice_0"), "Label").text);
        }

        [Test]
        public void ApplyStyle_SkipsColorOnTemplatedHistoryEntries()
        {
            Text(_sourceCanvas.transform, "EntryTemplate").color = Color.red;
            var ui = BuildUi();

            var so = new SerializedObject(_cfg);
            so.FindProperty("textColor").colorValue = Color.blue;
            so.ApplyModifiedPropertiesWithoutUndo();

            ui.ShowHistory(new[] { ("学者", "第一句") });
            ui.ApplyStyle(_cfg); // StartDialogue 换样式时对存量条目就地重刷

            Assert.AreEqual(Color.red, Text(_host.transform, "Entry_0").color, "模板条目换肤不动颜色");
            Assert.AreEqual(Color.blue, Text(_host.transform, "Title").color, "非模板节点照常换肤（证明 ApplyStyle 确实跑了）");
        }
    }
}
