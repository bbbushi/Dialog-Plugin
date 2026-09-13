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

    private bool _layoutFold = true;
    private bool _panelFold = true;
    private bool _plateFold = true;
    private bool _portraitFold = true;
    private bool _textFold = true;
    private bool _choiceFold = true;
    private bool _speedFold = true;

    // 缓存（projectChanged 失效）——防 Inspector 重绘期间反复 FindAssets 扫盘
    private int _referenceCount = -1;
    private List<TMP_FontAsset> _fonts;
    private string _layoutCheckMessage;
    private bool _layoutCheckIsError;

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

        _layoutFold = EditorGUILayout.Foldout(_layoutFold, "布局", true);
        if (_layoutFold)
        {
            EditorGUI.indentLevel++;
            DrawLayoutSection();
            EditorGUI.indentLevel--;
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

        _choiceFold = EditorGUILayout.Foldout(_choiceFold, "选项按钮", true);
        if (_choiceFold)
        {
            EditorGUI.indentLevel++;
            DrawColorField("choiceColor", "选项底色");
            DrawColorField("choiceTextColor", "选项文字色");
            EditorGUI.indentLevel--;
        }

        _speedFold = EditorGUILayout.Foldout(_speedFold, "语速", true);
        if (_speedFold)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.Slider(serializedObject.FindProperty("charactersPerSecond"), 5f, 120f, "打字速度（字/秒）");
            EditorGUILayout.Slider(serializedObject.FindProperty("autoAdvanceDelay"), 0.1f, 10f, "自动播放间隔（秒）");
            EditorGUILayout.Slider(serializedObject.FindProperty("readSpeedMultiplier"), 1f, 10f, "已读加速倍率");
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>
    /// 布局覆盖区：挂预制体 = 策划自由布置接管（按节点名解析）；空 = 代码默认布局。
    /// 「导出」用导出工具生成可编辑模板，改坏必需节点时运行时自动回退默认并报错。
    /// </summary>
    private void DrawLayoutSection()
    {
        var prop = serializedObject.FindProperty("layoutPrefab");
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(prop, new GUIContent("布局预制体（空 = 代码默认）"));
        if (EditorGUI.EndChangeCheck())
        {
            _layoutCheckMessage = null; // 换了目标，旧校验结论作废
        }

        if (GUILayout.Button("导出当前 UI 为预制体…", EditorStyles.miniButton))
        {
            DialogueUIExportTool.Export(); // 导出后字段被写回，下帧重绘即显示
        }

        using (new EditorGUI.DisabledScope(prop.objectReferenceValue == null))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("检查布局预制体", EditorStyles.miniButton))
                {
                    CheckLayoutPrefab(prop);
                }

                if (GUILayout.Button("恢复默认", EditorStyles.miniButton))
                {
                    prop.objectReferenceValue = null;
                    _layoutCheckMessage = null;
                }
            }
        }

        if (prop.objectReferenceValue == null)
        {
            EditorGUILayout.LabelField("当前为代码默认布局。想自由摆放节点/加装饰：先「导出」得到模板预制体再改。", EditorStyles.miniLabel);
        }
        else if (_layoutCheckMessage != null)
        {
            EditorGUILayout.HelpBox(_layoutCheckMessage, _layoutCheckIsError ? MessageType.Error : MessageType.Info);
        }
    }

    private void CheckLayoutPrefab(SerializedProperty prop)
    {
        if (prop.objectReferenceValue is not GameObject prefab)
        {
            return;
        }

        var notes = new List<string>();
        var problems = DialogueUILayoutContract.Validate(prefab, notes);
        if (problems.Count > 0)
        {
            _layoutCheckIsError = true;
            _layoutCheckMessage = "校验未通过（运行时会回退默认布局）：\n" + string.Join("\n", problems);
            return;
        }

        _layoutCheckIsError = false;
        _layoutCheckMessage = notes.Count == 0
            ? "✓ 校验通过：必需节点齐全，无降级项。"
            : "✓ 校验通过。降级运行项：\n" + string.Join("\n", notes);
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
