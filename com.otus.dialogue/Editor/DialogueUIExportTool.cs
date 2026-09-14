using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UI 布局预制体工具：把代码默认 UI 导出成可自由编辑的预制体模板并接入 DialogueUIConfig；
/// 「恢复默认布局」清空字段回到代码搭建。布局归预制体（人为布置），皮肤（字体/颜色/贴图）仍归配置。
/// </summary>
public static class DialogueUIExportTool
{
    [MenuItem("Dialogue/UI/导出当前 UI 为预制体…")]
    public static void Export()
    {
        var config = LoadConfig();
        if (config == null)
        {
            return;
        }

        string configDir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(config));
        string path = EditorUtility.SaveFilePanelInProject(
            "导出 UI 布局预制体", "DialogueUILayout", "prefab",
            "选择保存位置（建议与配置同目录）", string.IsNullOrEmpty(configDir) ? "Assets" : configDir);
        if (string.IsNullOrEmpty(path))
        {
            return; // 取消
        }

        var tempRoot = new GameObject("DialogueUIExportRoot");
        DialogueUIConfig styleOnly = null;
        try
        {
            // 用样式副本（清掉 layoutPrefab）强制走代码默认布局：导出的是「出厂模板」，
            // 即便当前配置已挂预制体，重复导出也得到干净的代码层级
            styleOnly = UnityEngine.Object.Instantiate(config);
            styleOnly.hideFlags = HideFlags.HideAndDontSave;
            var so = new SerializedObject(styleOnly);
            so.FindProperty("layoutPrefab").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            var ui = new DialogueUI();
            ui.Build(tempRoot.transform, styleOnly);
            DialogueUI.AppendDefaultTemplates(tempRoot.transform.GetChild(0).transform); // 出厂预制体自带可定制条目模板
            PrefabUtility.SaveAsPrefabAsset(tempRoot.transform.GetChild(0).gameObject, path);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(styleOnly);
            UnityEngine.Object.DestroyImmediate(tempRoot);
        }

        // 写回配置（此后 Build 优先实例化这份预制体）
        var configSo = new SerializedObject(config);
        configSo.FindProperty("layoutPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        configSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[Dialogue] UI 布局预制体已导出并接入配置：{path}\n" +
                  "现在可在预制体里自由改布局/加装饰（别改节点名）。删可选节点只是对应功能降级；" +
                  "清空配置里的「布局预制体」字段即回代码默认。\n" +
                  "预制体内附带未激活的 ChoiceTemplate/EntryTemplate（选项按钮/历史条目模板）：保持未激活，" +
                  "改其配色/字号/结构即改对应条目观感；删除模板则该条目回代码生成（配色走配置）。字体资产仍归配置统一。");
    }

    [MenuItem("Dialogue/UI/恢复默认布局")]
    public static void ResetToDefault()
    {
        var config = LoadConfig();
        if (config == null)
        {
            return;
        }

        var so = new SerializedObject(config);
        if (so.FindProperty("layoutPrefab").objectReferenceValue == null)
        {
            Debug.Log("[Dialogue] 当前本就是代码默认布局，无需恢复。");
            return;
        }

        so.FindProperty("layoutPrefab").objectReferenceValue = null;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        Debug.Log("[Dialogue] 已恢复代码默认布局（预制体文件保留未删，随时可再挂回）。");
    }

    [MenuItem("Dialogue/UI/检查布局预制体")]
    public static void CheckCurrent()
    {
        var config = LoadConfig();
        if (config == null)
        {
            return;
        }

        var so = new SerializedObject(config);
        if (so.FindProperty("layoutPrefab").objectReferenceValue is not GameObject prefab)
        {
            Debug.Log("[Dialogue] 未配置布局预制体，当前为代码默认布局。");
            return;
        }

        var notes = new List<string>();
        var problems = DialogueUILayoutContract.Validate(prefab, notes);
        if (problems.Count > 0)
        {
            Debug.LogError($"[Dialogue] 布局预制体校验未通过（运行时会回退默认布局）：\n{string.Join("\n", problems)}", prefab);
        }
        else
        {
            string degraded = notes.Count > 0 ? $"降级项：{string.Join("；", notes)}" : "无可选降级项。";
            Debug.Log($"[Dialogue] 布局预制体校验通过。{degraded}", prefab);
        }

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    private static DialogueUIConfig LoadConfig()
    {
        var config = Resources.Load<DialogueUIConfig>(DialogueManager.ConfigResourcePath);
        if (config == null)
        {
            Debug.LogError("[Dialogue] 未找到 DialogueUIConfig，请先运行菜单 Dialogue > Setup All。");
        }

        return config;
    }
}
