using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Dialogue.Tests.EditMode
{
    /// <summary>
    /// DialogueDirector 选章逻辑（FindNextPlayableIndex）的 EditMode 单元测试。
    /// 章节推进/串章依赖运行时状态机（PlayMode），这里只测纯函数部分。
    /// </summary>
    public class DialogueDirectorTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
            }

            _created.Clear();
        }

        /// <summary>造一个有 nodeCount 个节点的 DialogueAsset（nodes 为私有序列化字段，SerializedObject 填充）。</summary>
        private DialogueAsset CreateAsset(int nodeCount)
        {
            var asset = ScriptableObject.CreateInstance<DialogueAsset>();
            _created.Add(asset);

            var so = new SerializedObject(asset);
            var nodes = so.FindProperty("nodes");
            nodes.arraySize = nodeCount;
            if (nodeCount > 0)
            {
                nodes.GetArrayElementAtIndex(0).FindPropertyRelative("id").stringValue = "n1";
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        // ---------- 空与边界 ----------

        [Test]
        public void FindNext_NullList_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, DialogueDirector.FindNextPlayableIndex(null, 0, new DialogueVariables()));
        }

        [Test]
        public void FindNext_EmptyList_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, DialogueDirector.FindNextPlayableIndex(new List<DialogueDirector.Chapter>(), 0, new DialogueVariables()));
        }

        [Test]
        public void FindNext_FromBeyondEnd_ReturnsMinusOne()
        {
            var chapters = new List<DialogueDirector.Chapter> { new(CreateAsset(1)) };
            Assert.AreEqual(-1, DialogueDirector.FindNextPlayableIndex(chapters, 1, new DialogueVariables()));
        }

        [Test]
        public void FindNext_NegativeFrom_ClampedToZero()
        {
            var chapters = new List<DialogueDirector.Chapter> { new(CreateAsset(1)) };
            Assert.AreEqual(0, DialogueDirector.FindNextPlayableIndex(chapters, -3, new DialogueVariables()));
        }

        // ---------- 空章跳过 ----------

        [Test]
        public void FindNext_NullAssetOrNullNodeAsset_Skipped()
        {
            var chapters = new List<DialogueDirector.Chapter>
            {
                new(null),              // 未接线
                new(CreateAsset(0)),    // 无节点（空对话）
                new(CreateAsset(1)),    // 可播
            };

            Assert.AreEqual(2, DialogueDirector.FindNextPlayableIndex(chapters, 0, new DialogueVariables()));
        }

        [Test]
        public void FindNext_AllChaptersEmpty_ReturnsMinusOne()
        {
            var chapters = new List<DialogueDirector.Chapter> { new(null), new(CreateAsset(0)) };
            Assert.AreEqual(-1, DialogueDirector.FindNextPlayableIndex(chapters, 0, new DialogueVariables()));
        }

        // ---------- 条件 ----------

        [Test]
        public void FindNext_NoCondition_MatchesImmediately()
        {
            var chapters = new List<DialogueDirector.Chapter> { new(CreateAsset(1)) };
            Assert.AreEqual(0, DialogueDirector.FindNextPlayableIndex(chapters, 0, new DialogueVariables()));
        }

        [Test]
        public void FindNext_ConditionFalse_SkipsToLaterChapter()
        {
            var chapters = new List<DialogueDirector.Chapter>
            {
                new(CreateAsset(1), "courage>=5"), // 不满足 → 跳过
                new(CreateAsset(1)),               // 兜底章
            };

            var vars = new DialogueVariables();
            vars.Set("courage", 1);
            Assert.AreEqual(1, DialogueDirector.FindNextPlayableIndex(chapters, 0, vars));

            vars.Set("courage", 7); // 条件满足时停在第 0 章（分歧：变量积累改变去向）
            Assert.AreEqual(0, DialogueDirector.FindNextPlayableIndex(chapters, 0, vars));
        }

        [Test]
        public void FindNext_AllConditionsFalse_ReturnsMinusOne()
        {
            var chapters = new List<DialogueDirector.Chapter> { new(CreateAsset(1), "courage>=5") };
            Assert.AreEqual(-1, DialogueDirector.FindNextPlayableIndex(chapters, 0, new DialogueVariables()));
        }

        [Test]
        public void FindNext_SyntaxError_FallsBackToUnconditional()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("条件解析失败"));

            var chapters = new List<DialogueDirector.Chapter> { new(CreateAsset(1), "courage") }; // 缺比较符
            Assert.AreEqual(0, DialogueDirector.FindNextPlayableIndex(chapters, 0, new DialogueVariables()));
        }

        // ---------- Chapter 数据 ----------

        [Test]
        public void Chapter_Ctor_SetsAssetAndCondition()
        {
            var asset = CreateAsset(1);
            var chapter = new DialogueDirector.Chapter(asset, "courage>=2");

            Assert.AreSame(asset, chapter.Asset);
            Assert.AreEqual("courage>=2", chapter.Condition);
            Assert.IsNull(new DialogueDirector.Chapter().Asset); // 无参构造（Unity 序列化用）
        }
    }
}
