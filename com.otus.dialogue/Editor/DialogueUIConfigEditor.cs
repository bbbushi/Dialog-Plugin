using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// DialogueUIConfig 的策划友好编辑器：
/// 分区展示（面板/姓名牌/头像框/文字/语速）、贴图带尺寸与九宫格信息 + 预览、
/// 字体用项目内 TMP 字体下拉（不必手拖资产）、引用计数提示（防误删被引用样式）。
/// </summary>
[CustomEditor(typeof(DialogueUIConfig))]
[CanEditMultipleObjects]
public class DialogueUIConfigEditor : Editor
{
    private const string GlobalDefaultPath = "Assets/Resources/DialogueUIConfig.asset";

    private bool _panelFold = true;
    private bool _plateFold = true;
    private bool _portraitFold = true;
    private bool _textFold = true;
    private bool _speedFold = true;

    // 缓存（projectChanged 失效）——防 Inspector 重绘期间反复 FindAssets 扫盘
    private int _referenceCount = -1;
    private List<TMP_FontAsset> _fonts;

    private void OnEnable()
    {
        EditorApplication.projectChanged += InvalidateCaches;
    }

    private void OnDisable()
    {
        EditorApplication.projectChanged -= InvalidateCaches;
    }

    private void InvalidateCaches()
    {
        _referenceCount = -1;
        _fonts = null;
        Repaint();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        if (targets.Length == 1)
        {
            DrawInfoBar();
        }

        _panelFold = EditorGUILayout.Foldout(_panelFold, "对话面板", true);
        if (_panelFold)
        {
            EditorGUI.indentLevel++;
            DrawSpriteField(serializedObject.FindProperty("panelSprite"), "面板背景（九宫格）");
            EditorGUI.indentLevel--;
        }

        _plateFold = EditorGUILayout.Foldout(_plateFold, "姓名牌", true);
        if (_plateFold)
        {
            EditorGUI.indentLevel++;
            DrawSpriteField(serializedObject.FindProperty("nameSprite"), "姓名牌背景（九宫格）");
            DrawColorField("speakerColor", "名字默认色（角色可在 Speaker 资产里覆盖）");
            EditorGUI.indentLevel--;
        }

        _portraitFold = EditorGUILayout.Foldout(_portraitFold, "头像框", true);
        if (_portraitFold)
        {
            EditorGUI.indentLevel++;
            DrawSpriteField(serializedObject.FindProperty("portraitFrameSprite"), "头像框背景（九宫格）");
            EditorGUI.indentLevel--;
        }

        _textFold = EditorGUILayout.Foldout(_textFold, "文字", true);
        if (_textFold)
        {
            EditorGUI.indentLevel++;
            DrawFontPopup(serializedObject.FindProperty("font"));
            DrawColorField("textColor", "正文颜色");
            EditorGUI.indentLevel--;
        }

        _speedFold = EditorGUILayout.Foldout(_speedFold, "语速", true);
        if (_speedFold)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.Slider(serializedObject.FindProperty("charactersPerSecond"), 5f, 120f, "打字速度（字/秒）");
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>顶部信息条：全局默认判定 / 被多少对话资产引用。</summary>
    private void DrawInfoBar()
    {
        if (AssetDatabase.GetAssetPath(target) == GlobalDefaultPath)
        {
            EditorGUILayout.HelpBox("此样式是全局默认（Resources/DialogueUIConfig），未设置样式覆盖的对话都会使用它。", MessageType.Info);
            return;
        }

        int count = CountReferences();
        if (count == 0)
        {
            EditorGUILayout.HelpBox("当前没有对话资产引用此样式（在 Dialogue 资产的「样式覆盖」里指定）。", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox($"被 {count} 个对话资产引用为样式覆盖。删除前请先解除引用。", MessageType.Info);
        }
    }

    private int CountReferences()
    {
        if (_referenceCount >= 0)
        {
            return _referenceCount;
        }

        _referenceCount = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:DialogueAsset"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<DialogueAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null && asset.StyleOverride == target)
            {
                _referenceCount++;
            }
        }

        return _referenceCount;
    }

    /// <summary>贴图字段 + 尺寸/九宫格信息行 + 60px 预览。</summary>
    private static void DrawSpriteField(SerializedProperty prop, string label)
    {
        EditorGUILayout.PropertyField(prop, new GUIContent(label));
        if (prop.objectReferenceValue is not Sprite sprite)
        {
            return;
        }

        Rect rect = sprite.rect;
        Vector4 border = sprite.border; // (x=L, y=B, z=R, w=T)
        EditorGUILayout.LabelField(
            $"尺寸 {(int)rect.width}×{(int)rect.height} · 九宫格 边距 L{(int)border.x} R{(int)border.z} T{(int)border.w} B{(int)border.y}",
            EditorStyles.miniLabel);

        Rect preview = GUILayoutUtility.GetRect(60f, 60f, GUILayout.Width(60f));
        // 直取 texture：依赖"未进 SpriteAtlas"前提（当前项目无图集）
        GUI.DrawTexture(preview, sprite.texture, ScaleMode.ScaleToFit, true);
    }

    private void DrawColorField(string propertyName, string label)
    {
        EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName), new GUIContent(label));
    }

    /// <summary>字体下拉：枚举项目内 TMP 字体资产 + 首项「（默认）」= null。</summary>
    private void DrawFontPopup(SerializedProperty prop)
    {
        EnsureFontCache();

        var current = prop.objectReferenceValue as TMP_FontAsset;
        int selected = 0;
        for (int i = 0; i < _fonts.Count; i++)
        {
            if (ReferenceEquals(_fonts[i], current))
            {
                selected = i + 1;
                break;
            }
        }

        var labels = new List<string> { "（默认）" };
        labels.AddRange(_fonts.Select(f => f.name));

        int picked = EditorGUILayout.Popup("字体", selected, labels.ToArray());
        if (picked != selected)
        {
            prop.objectReferenceValue = picked == 0 ? null : _fonts[picked - 1];
        }
    }

    private void EnsureFontCache()
    {
        if (_fonts != null)
        {
            return;
        }

        _fonts = new List<TMP_FontAsset>();
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (font != null)
            {
                _fonts.Add(font);
            }
        }
    }
}
