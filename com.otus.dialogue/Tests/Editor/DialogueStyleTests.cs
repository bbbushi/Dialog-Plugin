using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Dialogue.Tests.EditMode
{
    /// <summary>DialogueAsset.ResolveStyle 的覆盖/回退语义（每对话样式分层）。</summary>
    public class DialogueStyleTests
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

        private T Create<T>() where T : ScriptableObject
        {
            var obj = ScriptableObject.CreateInstance<T>();
            created.Add(obj);
            return obj;
        }

        [Test]
        public void ResolveStyle_OverrideSet_ReturnsOverride()
        {
            var asset = Create<DialogueAsset>();
            var globalDefault = Create<DialogueUIConfig>();
            var overrideStyle = Create<DialogueUIConfig>();

            var so = new SerializedObject(asset);
            so.FindProperty("styleOverride").objectReferenceValue = overrideStyle;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(asset.ResolveStyle(globalDefault), Is.SameAs(overrideStyle));
        }

        [Test]
        public void ResolveStyle_OverrideNull_ReturnsGlobalDefault()
        {
            var asset = Create<DialogueAsset>();
            var globalDefault = Create<DialogueUIConfig>();

            Assert.That(asset.ResolveStyle(globalDefault), Is.SameAs(globalDefault));
        }

        [Test]
        public void ResolveStyle_BothNull_ReturnsNull()
        {
            // 覆盖"未跑 Setup、无全局默认"的极端路径
            var asset = Create<DialogueAsset>();

            Assert.That(asset.ResolveStyle(null), Is.Null);
        }
    }
}
