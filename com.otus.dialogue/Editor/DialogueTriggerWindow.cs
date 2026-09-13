using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 对话启动管理器：可视化管理场景中的对话启动方式。
/// 上区「全局入口」= DialogueDirector（剧本导演，视觉小说章节串播）；
/// 下区 = 局部触发器（DialogueTrigger / DialogueDemoTrigger）列表总览 + 快速接线
/// （换对话/改触发方式，SerializedProperty 路线免费 Undo）+ 定位 Ping / ▶ 预览 / ✕ 移除。
/// hierarchy 变化自动刷新。
/// </summary>
public class DialogueTriggerWindow : EditorWindow
{
    private struct Entry
    {
        public Component Component; // DialogueTrigger 或 DialogueDemoTrigger（共有的 "dialogue" 字段统一编辑）
        public GameObject Go;
    }

    private static readonly GUIContent DialogueLabel = new GUIContent("对话");
    private static readonly GUIContent ModeLabel = new GUIContent("方式");

    private readonly List<Entry> _entries = new List<Entry>();
    private readonly List<DialogueDirector> _directors = new List<DialogueDirector>();
    private Vector2 _scroll;

    [MenuItem("Dialogue/Trigger Manager")]
    public static void Open()
    {
        var window = GetWindow<DialogueTriggerWindow>("对话启动管理器");
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
        _entries.Clear();
        foreach (var trigger in FindObjectsByType<DialogueTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            _entries.Add(new Entry { Component = trigger, Go = trigger.gameObject });
        }

        foreach (var demo in FindObjectsByType<DialogueDemoTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            _entries.Add(new Entry { Component = demo, Go = demo.gameObject });
        }

        _entries.Sort((a, b) => string.CompareOrdinal(a.Go.name, b.Go.name));

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
        DrawDirectorsSection();

        if (_entries.Count == 0)
        {
            if (_directors.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "当前场景没有任何对话入口。\n① 视觉小说全局入口：点上方「＋ 全局入口」（剧本导演，章节自动串播）；\n"
                    + "② 局部触发：选中物体点「＋ 添加触发器」，或手动 Add Component > Dialogue > Dialogue Trigger。", MessageType.Info);
            }

            return;
        }

        EditorGUILayout.LabelField($"场景中共 {_entries.Count} 个对话触发器", EditorStyles.miniLabel);
        EditorGUILayout.Space(2);

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUI.skin.box);
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Component == null)
            {
                continue; // 行内按钮刚删掉的残留（Refresh 会重建，这里兜底防空引用）
            }

            DrawEntry(_entries[i]);
            EditorGUILayout.Space(3);
        }

        EditorGUILayout.EndScrollView();
    }

    // ---------- 工具栏 ----------

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            int selected = Selection.gameObjects.Length;
            using (new EditorGUI.DisabledScope(selected == 0))
            {
                if (GUILayout.Button(selected > 1 ? $"＋ 添加触发器（{selected} 个物体）" : "＋ 添加触发器（选中物体）",
                        EditorStyles.toolbarButton))
                {
                    AddToSelection();
                }
            }

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

    // ---------- 全局入口（剧本导演）----------

    private void DrawDirectorsSection()
    {
        if (_directors.Count == 0)
        {
            return;
        }

        EditorGUILayout.LabelField("全局入口（剧本导演）", EditorStyles.boldLabel);

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
        EditorGUILayout.Space(4);
    }

    /// <summary>给每个选中物体挂 DialogueTrigger（已有则跳过）；默认「靠近+按键」模式顺手补 Trigger BoxCollider。</summary>
    private static void AddToSelection()
    {
        Undo.SetCurrentGroupName("添加对话触发器");
        int group = Undo.GetCurrentGroup();

        int added = 0;
        foreach (var go in Selection.gameObjects)
        {
            if (go.GetComponent<DialogueTrigger>() != null)
            {
                continue;
            }

            Undo.AddComponent<DialogueTrigger>(go);
            if (go.GetComponent<Collider>() == null)
            {
                var box = Undo.AddComponent<BoxCollider>(go);
                box.isTrigger = true;
                box.size = new Vector3(2f, 2f, 2f);
            }

            added++;
        }

        Undo.CollapseUndoOperations(group); // 组件+Collider 一步撤销

        if (added > 0)
        {
            Debug.Log($"[Dialogue] 已给 {added} 个物体挂 DialogueTrigger（默认「靠近 + 按键」，已补 Trigger BoxCollider）。"
                + "在「对话启动管理器」或 Inspector 里接线对话资产，改「自动播放」模式可去掉交互。记得保存场景。");
        }
    }

    // ---------- 行卡片 ----------

    private void DrawEntry(Entry entry)
    {
        var so = new SerializedObject(entry.Component);
        var dialogueProp = so.FindProperty("dialogue");
        var trigger = entry.Component as DialogueTrigger;
        bool missingDialogue = dialogueProp.objectReferenceValue == null;
        bool missingCollider = trigger != null && trigger.Mode == DialogueTrigger.TriggerMode.InteractKey
            && entry.Go.GetComponent<Collider>() == null;

        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            // 第一行：物体名（点击定位）+ 类型徽标 + 状态警示
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(entry.Go.name, EditorStyles.boldLabel, GUILayout.MinWidth(120)))
                {
                    Selection.activeGameObject = entry.Go;
                    EditorGUIUtility.PingObject(entry.Go);
                }

                GUILayout.Label(trigger != null ? "DialogueTrigger" : "点击（Demo）", EditorStyles.miniButton, GUILayout.Width(76));

                if (!((Behaviour)entry.Component).enabled)
                {
                    GUILayout.Label("已停用", EditorStyles.miniLabel, GUILayout.Width(38));
                }

                GUILayout.FlexibleSpace();

                if (missingDialogue)
                {
                    GUI.color = Color.red;
                    GUILayout.Label("⚠ 未指定对话", EditorStyles.miniLabel);
                    GUI.color = Color.white;
                }
                else if (missingCollider)
                {
                    GUI.color = Color.yellow;
                    GUILayout.Label("⚠ 缺 Collider", EditorStyles.miniLabel);
                    GUI.color = Color.white;
                }
            }

            // 第二行：对话资产 + 触发方式 + 操作
            using (new EditorGUILayout.HorizontalScope())
            {
                so.Update();
                EditorGUILayout.PropertyField(dialogueProp, DialogueLabel, GUILayout.MinWidth(140));

                if (trigger != null)
                {
                    EditorGUILayout.PropertyField(so.FindProperty("mode"), ModeLabel, GUILayout.Width(170));
                }

                so.ApplyModifiedProperties();

                using (new EditorGUI.DisabledScope(missingDialogue))
                {
                    if (GUILayout.Button("▶ 预览", EditorStyles.miniButton, GUILayout.Width(52)))
                    {
                        DialoguePreviewWindow.Open((DialogueAsset)dialogueProp.objectReferenceValue);
                    }
                }

                if (GUILayout.Button("⌖ 定位", EditorStyles.miniButton, GUILayout.Width(52)))
                {
                    Selection.activeGameObject = entry.Go;
                    EditorGUIUtility.PingObject(entry.Go);
                }

                if (GUILayout.Button("✕ 移除", EditorStyles.miniButton, GUILayout.Width(52)))
                {
                    Undo.DestroyObjectImmediate(entry.Component);
                    Refresh();
                }
            }

            if (missingCollider)
            {
                EditorGUILayout.HelpBox("「靠近 + 按键」需要一个勾选 Is Trigger 的 Collider 才能感应玩家。", MessageType.Warning);
            }
        }
    }
}
