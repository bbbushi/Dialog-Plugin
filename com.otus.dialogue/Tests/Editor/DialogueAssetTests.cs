using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Dialogue.Tests.EditMode
{
    /// <summary>
    /// DialogueAsset 数据访问逻辑的 EditMode 单元测试。
    /// 通过 SerializedObject 填充私有 [SerializeField] nodes 字段，贴近真实资产数据。
    /// </summary>
    public class DialogueAssetTests
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

        /// <summary>构造 DialogueAsset 并按 (id, nextId) 列表填充私有 nodes 字段。</summary>
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

        // ---------- GetStartNode ----------

        [Test]
        public void GetStartNode_EmptyNodes_ReturnsNull()
        {
            var asset = CreateAsset();

            Assert.That(asset.GetStartNode(), Is.Null);
        }

        [Test]
        public void GetStartNode_NonEmpty_ReturnsFirstNode()
        {
            var asset = CreateAsset(("n1", ""), ("n2", ""), ("n3", ""));

            var start = asset.GetStartNode();

            Assert.That(start, Is.Not.Null);
            Assert.That(start.Id, Is.EqualTo("n1"));
            Assert.That(start, Is.SameAs(asset.FindNode("n1")));
        }

        // ---------- FindNode ----------

        [Test]
        public void FindNode_ExistingId_ReturnsMatchingNode()
        {
            var asset = CreateAsset(("n1", ""), ("n2", ""), ("n3", ""));

            var found = asset.FindNode("n2");

            Assert.That(found, Is.Not.Null);
            Assert.That(found.Id, Is.EqualTo("n2"));
        }

        [Test]
        public void FindNode_MissingId_ReturnsNullAndWarns()
        {
            var asset = CreateAsset(("n1", ""), ("n2", ""));
            LogAssert.Expect(LogType.Warning, new Regex(@"找不到节点 id=""missing"""));

            Assert.That(asset.FindNode("missing"), Is.Null);
        }

        // ---------- GetNext：nextId 为空 = 顺序推进 ----------

        [Test]
        public void GetNext_EmptyNextId_MiddleNode_ReturnsSequentialNext()
        {
            var asset = CreateAsset(("n1", ""), ("n2", ""), ("n3", ""));
            var current = asset.FindNode("n1");

            var next = asset.GetNext(current);

            Assert.That(next, Is.Not.Null);
            Assert.That(next.Id, Is.EqualTo("n2"));
        }

        [Test]
        public void GetNext_EmptyNextId_LastNode_ReturnsNull()
        {
            var asset = CreateAsset(("n1", ""), ("n2", ""), ("n3", ""));
            var last = asset.FindNode("n3");

            Assert.That(asset.GetNext(last), Is.Null);
        }

        // ---------- GetNext：nextId 非空 = 跳转 ----------

        [Test]
        public void GetNext_NonEmptyNextId_JumpsForwardToTarget()
        {
            var asset = CreateAsset(("n1", "n3"), ("n2", ""), ("n3", ""));
            var current = asset.FindNode("n1");

            var next = asset.GetNext(current);

            Assert.That(next, Is.Not.Null);
            Assert.That(next.Id, Is.EqualTo("n3"));
        }

        [Test]
        public void GetNext_NonEmptyNextId_JumpsBackwardToTarget()
        {
            var asset = CreateAsset(("n1", ""), ("n2", ""), ("n3", "n1"));
            var current = asset.FindNode("n3");

            var next = asset.GetNext(current);

            Assert.That(next, Is.Not.Null);
            Assert.That(next.Id, Is.EqualTo("n1"));
        }

        [Test]
        public void GetNext_NonEmptyNextId_TargetMissing_ReturnsNullAndWarns()
        {
            var asset = CreateAsset(("n1", "ghost"), ("n2", ""));
            var current = asset.FindNode("n1");
            LogAssert.Expect(LogType.Warning, new Regex(@"找不到节点 id=""ghost"""));

            Assert.That(asset.GetNext(current), Is.Null);
        }

        // ---------- 边界 ----------

        [Test]
        public void GetNext_NullCurrent_ReturnsNull()
        {
            var asset = CreateAsset(("n1", ""), ("n2", ""));

            Assert.That(asset.GetNext(null), Is.Null);
        }

        // ---------- ResolvedSpeakerName：speaker 资产 > 旧 speakerName > null（旁白）----------

        private DialogueAsset CreateSingleNode(string legacyName, SpeakerAsset speaker)
        {
            var asset = ScriptableObject.CreateInstance<DialogueAsset>();
            created.Add(asset);

            var so = new SerializedObject(asset);
            var nodes = so.FindProperty("nodes");
            nodes.arraySize = 1;
            var node = nodes.GetArrayElementAtIndex(0);
            node.FindPropertyRelative("id").stringValue = "n1";
            node.FindPropertyRelative("speakerName").stringValue = legacyName;
            node.FindPropertyRelative("speaker").objectReferenceValue = speaker;
            node.FindPropertyRelative("text").stringValue = "文本";
            node.FindPropertyRelative("nextId").stringValue = string.Empty;
            so.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        private SpeakerAsset CreateSpeaker(string assetName, string displayName)
        {
            var s = ScriptableObject.CreateInstance<SpeakerAsset>();
            created.Add(s);
            s.name = assetName; // CreateInstance 后 name 为空串，回退测试必须显式设
            var so = new SerializedObject(s);
            so.FindProperty("displayName").stringValue = displayName;
            so.ApplyModifiedPropertiesWithoutUndo();
            return s;
        }

        [Test]
        public void ResolvedSpeakerName_WithSpeakerAsset_UsesDisplayName()
        {
            var speaker = CreateSpeaker("资产名", "旅人");
            var asset = CreateSingleNode("旧名字", speaker); // 旧字段被资产接管

            Assert.That(asset.GetStartNode().ResolvedSpeakerName, Is.EqualTo("旅人"));
        }

        [Test]
        public void ResolvedSpeakerName_SpeakerAssetEmptyName_FallsBackToAssetName()
        {
            var speaker = CreateSpeaker("角色A", "");
            var asset = CreateSingleNode("", speaker);

            Assert.That(asset.GetStartNode().ResolvedSpeakerName, Is.EqualTo("角色A"));
        }

        [Test]
        public void ResolvedSpeakerName_NoAsset_UsesLegacyName()
        {
            var asset = CreateSingleNode("旧名", null);

            Assert.That(asset.GetStartNode().ResolvedSpeakerName, Is.EqualTo("旧名"));
        }

        [Test]
        public void ResolvedSpeakerName_BothEmpty_ReturnsEmpty()
        {
            var asset = CreateSingleNode("", null);

            // 旁白：speaker 为空 + 旧名为空串（调用方统一用 IsNullOrEmpty 判定）
            Assert.That(asset.GetStartNode().ResolvedSpeakerName, Is.Empty);
        }
    }
}
