using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Dialogue.Tests.EditMode
{
    /// <summary>SpeakerAsset 显示名回退链与名字颜色覆盖语义。</summary>
    public class SpeakerAssetTests
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

        private SpeakerAsset CreateSpeaker(string assetName, string displayName, bool overrideColor, Color color)
        {
            var speaker = ScriptableObject.CreateInstance<SpeakerAsset>();
            created.Add(speaker);
            speaker.name = assetName; // CreateInstance 后 name 为空串——测"回退资产名"必须显式设

            var so = new SerializedObject(speaker);
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("overrideNameColor").boolValue = overrideColor;
            so.FindProperty("nameColor").colorValue = color;
            so.ApplyModifiedPropertiesWithoutUndo();
            return speaker;
        }

        [Test]
        public void GetDisplayName_HasDisplayName_ReturnsIt()
        {
            var speaker = CreateSpeaker("资产名", "旅人", false, Color.white);

            Assert.That(speaker.GetDisplayName(), Is.EqualTo("旅人"));
        }

        [Test]
        public void GetDisplayName_EmptyOrWhitespace_ReturnsAssetName()
        {
            var empty = CreateSpeaker("角色A", "", false, Color.white);
            var whitespace = CreateSpeaker("角色B", "   ", false, Color.white);

            Assert.That(empty.GetDisplayName(), Is.EqualTo("角色A"));
            Assert.That(whitespace.GetDisplayName(), Is.EqualTo("角色B"));
        }

        [Test]
        public void ResolveNameColor_OverrideTrue_ReturnsOverride()
        {
            var speaker = CreateSpeaker("角色", "名", true, Color.cyan);
            Assert.That(speaker.ResolveNameColor(Color.red), Is.EqualTo(Color.cyan));
        }

        [Test]
        public void ResolveNameColor_OverrideFalse_ReturnsFallback()
        {
            var speaker = CreateSpeaker("角色", "名", false, Color.cyan);
            Assert.That(speaker.ResolveNameColor(Color.red), Is.EqualTo(Color.red));
        }
    }
}
