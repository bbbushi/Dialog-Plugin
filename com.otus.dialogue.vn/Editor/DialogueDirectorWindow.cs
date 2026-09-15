using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 剧本导演管理器（视觉小说全局入口）：总览场景中的 DialogueDirector，
/// 一键「＋ 全局入口」（选中物体挂或新建空物体）、定位 Ping / ✕ 移除。
/// 章节列表与每章条件在物体 Inspector 里编辑。hierarchy 变化自动刷新。
/// 局部触发器的管理窗口在 interaction 扩展包的 Trigger Manager。
/// </summary>
public class DialogueDirectorWindow : EditorWindow
{
    private readonly List<DialogueDirector> _directors = new List<DialogueDirector>();

    [MenuItem("Dialogue/Director Manager")]
    public static void Open()
    {
        var window = GetWindow<DialogueDirectorWindow>("剧本导演管理器");
        window.Refresh();
    }

    private void OnEnable()
    {
        EditorApplication.hierarchyChanged += Refresh; // 加删组件/物体/换场景自动刷新
        Refresh();
    }

    private void OnDisable()
    {
        EditorApplication.hierarchyChanged -= Refresh; // 严格配对，防泄漏
    }

    private void Refresh()
    {
        _directors.Clear();
        foreach (var director in FindObjectsByType<DialogueDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            _directors.Add(director);
        }

        Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (_directors.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "当前场景没有剧本导演（视觉小说全局入口）。\n点上方「＋ 全局入口」创建（无需具体位置），"
                + "在其 Inspector 里按顺序拖入章节对话资产。\n（单点交互式对话触发请安装 com.otus.dialogue.interaction 扩展包。）",
                MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField($"场景中共 {_directors.Count} 个剧本导演", EditorStyles.miniLabel);
        EditorGUILayout.Space(2);

        foreach (var director in _directors.ToList()) // 快照遍历：行内「移除」会 Refresh 清空重填原列表，枚举器会抛 Collection was modified
        {
            if (director == null)
            {
                continue; // 行内按钮刚删掉的残留
            }

            using (new EditorGUILayout.HorizontalScope(GUI.skin.box))
            {
                if (GUILayout.Button(director.gameObject.name, EditorStyles.boldLabel, GUILayout.MinWidth(100)))
                {
                    Selection.activeGameObject = director.gameObject;
                    EditorGUIUtility.PingObject(director.gameObject);
                }

                GUILayout.Label($"剧本导演 · {director.ChapterCount} 章", EditorStyles.miniLabel, GUILayout.Width(110));

                if (!((Behaviour)director).enabled)
                {
                    GUILayout.Label("已停用", EditorStyles.miniLabel, GUILayout.Width(38));
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("⌖ 定位", EditorStyles.miniButton, GUILayout.Width(52)))
                {
                    Selection.activeGameObject = director.gameObject;
                    EditorGUIUtility.PingObject(director.gameObject);
                }

                if (GUILayout.Button("✕ 移除", EditorStyles.miniButton, GUILayout.Width(52)))
                {
                    Undo.DestroyObjectImmediate(director);
                    Refresh();
                }
            }
        }

        GUILayout.Label("章节列表与每章条件在物体 Inspector 里编辑（选中后按顺序拖入对话资产）", EditorStyles.miniLabel);
    }

    // ---------- 工具栏 ----------

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("＋ 全局入口", EditorStyles.toolbarButton))
            {
                AddDirector();
            }

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(44)))
            {
                Refresh();
            }
        }
    }

    /// <summary>挂剧本导演：选中物体则挂其上，未选中则新建「Dialogue Director」物体（全局入口无需具体位置）。</summary>
    private static void AddDirector()
    {
        Undo.SetCurrentGroupName("添加剧本导演");
        int group = Undo.GetCurrentGroup();

        GameObject go = Selection.activeGameObject;
        bool createdGo = false;
        if (go == null)
        {
            go = new GameObject("Dialogue Director");
            Undo.RegisterCreatedObjectUndo(go, "新建剧本导演");
            createdGo = true;
        }

        if (go.GetComponent<DialogueDirector>() != null)
        {
            Debug.LogWarning($"[Dialogue] {go.name} 已挂 DialogueDirector。");
            if (createdGo)
            {
                Undo.DestroyObjectImmediate(go); // 刚建的空壳没加成组件，撤回
            }

            return;
        }

        Undo.AddComponent<DialogueDirector>(go);
        Undo.CollapseUndoOperations(group);

        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log("[Dialogue] 已挂 DialogueDirector（视觉小说全局入口）。在其 Inspector 里按顺序拖入章节对话资产；"
            + "每章可写条件（如 courage>=5，不满足自动跳过——同位多条做分歧结局）；playOnStart 默认开场自动播，播完触发 Finished 事件。记得保存场景。");
    }
}
