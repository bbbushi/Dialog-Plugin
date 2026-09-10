using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// DialogueAsset 的策划友好 Inspector：
/// 卡片式节点编辑 + 下一句下拉选择（消除断链）+ id 重命名自动同步引用 + 常驻校验徽标 + 一键预览。
/// 全程 SerializedProperty 路线（Undo/多资产脏标记免费）。
/// </summary>
[CustomEditor(typeof(DialogueAsset))]
[CanEditMultipleObjects]
public class DialogueAssetEditor : Editor
{
    private SerializedProperty _nodes;
    private readonly Dictionary<string, bool> _foldoutById = new Dictionary<string, bool>();
    private readonly Dictionary<int, Rect> _cardRects = new Dictionary<int, Rect>();
    private Vector2 _scroll;
    private bool _showIssues;
    private int _pendingScrollToIndex = -1;
    private string _lastSyncMessage;
    private string _lastSyncForId;
    private List<DialogueIssue> _cachedIssues;

    private static readonly GUIContent IdLabel = new GUIContent("节点 ID");
    private static readonly GUIContent NextLabel = new GUIContent("下一句");

    private void OnEnable()
    {
        _nodes = serializedObject.FindProperty("nodes");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        if (targets.Length > 1)
        {
            DrawMultiSelectionNotice();
            return;
        }

        var asset = (DialogueAsset)target;

        // 文本编辑中沿用上次校验结果：若逐字符重算，首个字符会让 EmptyText 等问题消失，
        // 校验区/卡片图标的控件数随之变化 → IMGUI control ID 漂移 → 焦点与输入法组合串丢失（打字丢字根因）
        if (_cachedIssues == null || !EditorGUIUtility.editingTextField)
        {
            _cachedIssues = DialogueValidator.Validate(asset);
        }

        var issues = _cachedIssues;

        EditorGUILayout.Space(2);
        DrawToolbar(asset);
        DrawStyleSection();
        DrawValidationSummary(asset, issues);

        _cardRects.Clear();
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUI.skin.box);
        for (int i = 0; i < _nodes.arraySize; i++)
        {
            DrawNodeCard(i, issues, _nodes.arraySize);
        }

        if (_nodes.arraySize == 0)
        {
            EditorGUILayout.HelpBox("对话为空。点击上方「＋ 添加节点」开始创作。", MessageType.Info);
        }

        EditorGUILayout.EndScrollView();

        serializedObject.ApplyModifiedProperties();

        // 滚动定位（校验项点击后）
        if (_pendingScrollToIndex >= 0 && _cardRects.TryGetValue(_pendingScrollToIndex, out var rect))
        {
            _scroll.y = Mathf.Max(0f, rect.y - 70f);
            _pendingScrollToIndex = -1;
            Repaint();
        }
    }

    // ---------- 工具栏 ----------

    private void DrawToolbar(DialogueAsset asset)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("＋ 添加节点", EditorStyles.toolbarButton))
            {
                AddNode();
            }

            if (GUILayout.Button("校验 ▷ Console", EditorStyles.toolbarButton))
            {
                foreach (var issue in DialogueValidator.Validate(asset))
                {
                    Debug.Log($"[对话校验] {asset.name}：{issue.Message}", asset);
                }
            }

            if (GUILayout.Button("▶ 预览", EditorStyles.toolbarButton))
            {
                DialoguePreviewWindow.Open(asset);
            }

            if (GUILayout.Button("打开节点图", EditorStyles.toolbarButton))
            {
                DialogueGraphWindow.Open(asset); // 同 asmdef 直接调用，无需反射
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"共 {_nodes.arraySize} 句", EditorStyles.miniLabel);
        }
    }

    // ---------- 样式覆盖（资产级，滚动区外）----------

    private void DrawStyleSection()
    {
        var styleProp = serializedObject.FindProperty("styleOverride");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(styleProp, new GUIContent("样式覆盖"));

            if (GUILayout.Button("创建新样式…", EditorStyles.miniButton, GUILayout.Width(86)))
            {
                CreateStyleAsset(styleProp);
            }

            if (GUILayout.Button("使用全局默认", EditorStyles.miniButton, GUILayout.Width(86)))
            {
                styleProp.objectReferenceValue = null;
            }
        }

        if (styleProp.objectReferenceValue == null)
        {
            EditorGUILayout.LabelField("当前使用全局默认样式（Resources/DialogueUIConfig）", EditorStyles.miniLabel);
        }
    }

    /// <summary>以全局默认为起点创建新样式资产并挂到当前对话（CopySerialized 预填熟悉样式）。</summary>
    private void CreateStyleAsset(SerializedProperty styleProp)
    {
        string dir = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(target));
        string path = EditorUtility.SaveFilePanelInProject("创建对话样式", "DialogueStyle", "asset", "保存新的对话样式", dir);
        if (string.IsNullOrEmpty(path))
        {
            return; // 取消
        }

        var globalDefault = Resources.Load<DialogueUIConfig>(DialogueManager.ConfigResourcePath);
        var created = CreateInstance<DialogueUIConfig>();
        if (globalDefault != null)
        {
            EditorUtility.CopySerialized(globalDefault, created); // 预填全局默认作起点
        }

        created.name = System.IO.Path.GetFileNameWithoutExtension(path); // CopySerialized 连 m_Name 一起拷，必须重设
        AssetDatabase.CreateAsset(created, path);
        AssetDatabase.SaveAssets();
        styleProp.objectReferenceValue = created;
        Debug.Log($"[Dialogue] 已创建对话样式：{path}（以全局默认为起点，可在其 Inspector 里调整）");
    }

    // ---------- 校验汇总（可点击定位）----------

    private void DrawValidationSummary(DialogueAsset asset, List<DialogueIssue> issues)
    {
        int errors = issues.Count(i => i.Severity == DialogueIssueSeverity.Error);
        int warnings = issues.Count - errors;

        if (issues.Count == 0)
        {
            EditorGUILayout.HelpBox("✓ 校验通过：无断链、无重复 id、无死循环。", MessageType.None);
            return;
        }

        _showIssues = EditorGUILayout.Foldout(_showIssues, $"校验：错误 {errors} · 警告 {warnings}", true);
        if (!_showIssues)
        {
            return;
        }

        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            foreach (var issue in issues)
            {
                var style = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleLeft };
                if (issue.Severity == DialogueIssueSeverity.Error)
                {
                    style.normal.textColor = Color.red;
                }

                if (GUILayout.Button($"{(issue.Severity == DialogueIssueSeverity.Error ? "✖" : "⚠")} {issue.Message}", style))
                {
                    if (issue.NodeIndex >= 0 && issue.NodeIndex < _nodes.arraySize)
                    {
                        var idProp = _nodes.GetArrayElementAtIndex(issue.NodeIndex).FindPropertyRelative("id");
                        _foldoutById[CardKey(issue.NodeIndex, idProp.stringValue)] = true; // 展开对应卡片
                        _pendingScrollToIndex = issue.NodeIndex;
                    }
                }
            }
        }
    }

    // ---------- 节点卡片 ----------

    private void DrawNodeCard(int i, List<DialogueIssue> issues, int total)
    {
        var node = _nodes.GetArrayElementAtIndex(i);
        var idProp = node.FindPropertyRelative("id");
        var speakerProp = node.FindPropertyRelative("speakerName");
        var speakerAssetProp = node.FindPropertyRelative("speaker");
        var textProp = node.FindPropertyRelative("text");
        var nextIdProp = node.FindPropertyRelative("nextId");
        var choicesProp = node.FindPropertyRelative("choices");
        int choiceCount = choicesProp != null ? choicesProp.arraySize : 0; // HasChoices 的 SerializedProperty 判定

        string key = CardKey(i, idProp.stringValue);
        bool expanded = _foldoutById.TryGetValue(key, out bool value) && value;

        // 卡片命中问题的最高级别
        var cardIssues = issues.Where(x => x.NodeIndex == i).ToList();
        bool hasError = cardIssues.Any(x => x.Severity == DialogueIssueSeverity.Error);

        using (new EditorGUILayout.VerticalScope(hasError ? ErrorBoxStyle : GUI.skin.box))
        {
            // ---- 头部：折叠箭头 + 摘要 + 问题图标 ----
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(expanded ? "▾" : "▸", GUILayout.Width(22)))
                {
                    _foldoutById[key] = !expanded;
                }

                string resolvedSpeaker = ResolveSpeakerDisplay(node);
                string summary = BuildSummary(i, idProp.stringValue, resolvedSpeaker, textProp.stringValue, nextIdProp.stringValue, total, choiceCount);
                if (GUILayout.Button(summary, EditorStyles.label, GUILayout.MinWidth(0)))
                {
                    _foldoutById[key] = !expanded;
                }

                if (cardIssues.Count > 0)
                {
                    var icon = EditorGUIUtility.IconContent(hasError ? "console.erroricon" : "console.warnicon");
                    GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
                }
            }

            // ---- 展开体 ----
            if (expanded)
            {
                EditorGUI.indentLevel++;
                DrawNodeIdField(idProp, i);
                EditorGUILayout.PropertyField(speakerAssetProp, new GUIContent("说话人（角色资产）"));
                if (speakerAssetProp.objectReferenceValue != null)
                {
                    EditorGUILayout.LabelField($"旧版名：{speakerProp.stringValue}（已由角色资产接管，未使用）", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.PropertyField(speakerProp, new GUIContent("说话人（纯文本·旧版）"));
                }

                EditorGUILayout.PropertyField(textProp, new GUIContent("正文"));

                // 选项接管后 nextId 不生效：置灰防误配
                using (new EditorGUI.DisabledScope(choiceCount > 0))
                {
                    DrawNextIdPopup(nextIdProp, i, total);
                }

                if (choiceCount > 0)
                {
                    EditorGUILayout.LabelField("已由选项接管", EditorStyles.miniLabel);
                }

                DrawChoicesSection(choicesProp, total);

                // id 重命名同步提示
                if (_lastSyncForId == idProp.stringValue && !string.IsNullOrEmpty(_lastSyncMessage))
                {
                    EditorGUILayout.HelpBox(_lastSyncMessage, MessageType.Info);
                }

                // 卡片命中的校验问题
                if (cardIssues.Count > 0)
                {
                    EditorGUILayout.Space(2);
                    foreach (var issue in cardIssues)
                    {
                        EditorGUILayout.LabelField($"{(issue.Severity == DialogueIssueSeverity.Error ? "✖" : "⚠")} {issue.Message}",
                            IssueLabelStyle(issue.Severity));
                    }
                }

                EditorGUILayout.Space(2);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginDisabledGroup(i == 0);
                    if (GUILayout.Button("上移", EditorStyles.miniButtonLeft, GUILayout.Width(48)))
                    {
                        _nodes.MoveArrayElement(i, i - 1);
                    }

                    EditorGUI.EndDisabledGroup();

                    EditorGUI.BeginDisabledGroup(i == total - 1);
                    if (GUILayout.Button("下移", EditorStyles.miniButtonMid, GUILayout.Width(48)))
                    {
                        _nodes.MoveArrayElement(i, i + 1);
                    }

                    EditorGUI.EndDisabledGroup();

                    if (GUILayout.Button("复制", EditorStyles.miniButtonMid, GUILayout.Width(48)))
                    {
                        DuplicateNode(i);
                    }

                    if (GUILayout.Button("删除", EditorStyles.miniButtonRight, GUILayout.Width(48)))
                    {
                        DeleteNode(i);
                        return; // 本帧后续绘制跳过（数组已变）
                    }

                    GUILayout.FlexibleSpace();
                }

                EditorGUI.indentLevel--;
            }
        }

        _cardRects[i] = GUILayoutUtility.GetLastRect();
    }

    /// <summary>折叠态头部摘要：#序号 id [说话人] 文本预览 → 下一句走向 [◆N选项]。纯字符串拼接。</summary>
    private static string BuildSummary(int i, string id, string speaker, string text, string nextId, int total, int choiceCount)
    {
        string displayId = string.IsNullOrEmpty(id) ? "（无 id）" : id;
        string displaySpeaker = string.IsNullOrEmpty(speaker) ? "旁白" : speaker;
        string preview = Truncate(text.Replace("\n", " "), 14);
        string next = DescribeNext(nextId, i, total, null);
        string choices = choiceCount > 0 ? $" ◆{choiceCount}选项" : string.Empty;
        return $"#{i + 1}  {displayId}  [{displaySpeaker}]  {preview}   {next}{choices}";
    }

    private static string DescribeNext(string nextId, int i, int total, List<string> allIds)
    {
        if (string.IsNullOrEmpty(nextId))
        {
            return i == total - 1 ? "■ 结束" : "→ 下句";
        }

        return $"→ 跳转 {nextId}";
    }

    // ---------- id 字段（Delayed + 引用自动同步）----------

    private void DrawNodeIdField(SerializedProperty idProp, int index)
    {
        string oldId = idProp.stringValue;

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.DelayedTextField(idProp, IdLabel); // 回车/失焦才提交，防逐字符触发同步
        if (EditorGUI.EndChangeCheck())
        {
            string newId = idProp.stringValue;
            if (!string.IsNullOrEmpty(oldId) && !string.IsNullOrEmpty(newId) && newId != oldId)
            {
                int synced = 0;
                for (int j = 0; j < _nodes.arraySize; j++)
                {
                    var nextProp = _nodes.GetArrayElementAtIndex(j).FindPropertyRelative("nextId");
                    if (nextProp.stringValue == oldId)
                    {
                        nextProp.stringValue = newId;
                        synced++;
                    }
                }

                if (synced > 0)
                {
                    Undo.SetCurrentGroupName($"重命名节点 id：{oldId} → {newId}"); // id + 引用同批 Apply = 一个 Undo 步
                    _lastSyncMessage = $"已同步 {synced} 处跳转引用（{oldId} → {newId}）";
                    _lastSyncForId = newId;
                }
            }

            // 折叠字典键跟随重命名，保持展开状态
            string oldKey = CardKey(index, oldId);
            if (_foldoutById.TryGetValue(oldKey, out bool wasExpanded))
            {
                _foldoutById.Remove(oldKey);
                _foldoutById[CardKey(index, newId)] = wasExpanded;
            }
        }
    }

    // ---------- 下一句下拉（核心防断链）----------

    private void DrawNextIdPopup(SerializedProperty nextIdProp, int i, int total)
    {
        // 每次重绘现算选项（Undo/重排/删除后文案自动正确）
        var labels = new List<string>();
        var values = new List<string>();

        bool isLast = i == total - 1;
        if (isLast)
        {
            labels.Add("■ 结束对话");
        }
        else
        {
            var nextNode = _nodes.GetArrayElementAtIndex(i + 1);
            string nextId = nextNode.FindPropertyRelative("id").stringValue;
            string nextSpeaker = ResolveSpeakerDisplay(nextNode); // speaker 资产 > 旧文本名 > 旁白
            labels.Add($"▶ 顺序 → 下一句 #{i + 2} · {(string.IsNullOrEmpty(nextId) ? "（无 id）" : nextId)} · {nextSpeaker}");
        }

        values.Add(string.Empty);

        for (int j = 0; j < total; j++)
        {
            if (j == i)
            {
                continue; // 排除自身：单后继模型下自跳必死循环，不给策划设陷阱
            }

            var other = _nodes.GetArrayElementAtIndex(j);
            string otherId = other.FindPropertyRelative("id").stringValue;
            if (string.IsNullOrEmpty(otherId))
            {
                continue; // 无 id 的节点无法被跳转引用
            }

            labels.Add($"跳转到 #{j + 1} · {otherId}：{Truncate(other.FindPropertyRelative("text").stringValue, 12)}");
            values.Add(otherId);
        }

        // 断链兜底：当前值非空但不在选项中（手改/外部改名）
        string current = nextIdProp.stringValue;
        bool broken = !string.IsNullOrEmpty(current) && !values.Contains(current);
        if (broken)
        {
            labels.Insert(0, $"⚠ 断链：{current}");
            values.Insert(0, current);
        }

        int selected = Mathf.Max(0, values.IndexOf(current)); // Popup 不接受 -1，断链态为 0

        var style = EditorStyles.popup;
        if (broken)
        {
            style = new GUIStyle(EditorStyles.popup);
            style.normal.textColor = Color.red;
        }

        var options = labels.Select(l => new GUIContent(l)).ToArray();
        EditorGUI.BeginChangeCheck();
        int picked = EditorGUILayout.Popup(NextLabel, selected, options, style);
        if (EditorGUI.EndChangeCheck() && picked >= 0 && picked < values.Count)
        {
            nextIdProp.stringValue = values[picked];
        }
    }

    // ---------- 玩家选项（choices 列表）----------

    private void DrawChoicesSection(SerializedProperty choicesProp, int total)
    {
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("玩家选项", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        for (int c = 0; c < choicesProp.arraySize; c++)
        {
            var choice = choicesProp.GetArrayElementAtIndex(c);
            var textProp = choice.FindPropertyRelative("text");
            var nextIdProp = choice.FindPropertyRelative("nextId");

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(textProp, GUIContent.none,
                    GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.55f)); // 文案占宽 ~60%，右侧留给跳转

                DrawChoiceTargetPopup(nextIdProp, total);

                if (GUILayout.Button("−", EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    choicesProp.DeleteArrayElementAtIndex(c);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var conditionProp = choice.FindPropertyRelative("condition");
                var setProp = choice.FindPropertyRelative("setExpressions");
                EditorGUILayout.PropertyField(conditionProp,
                    new GUIContent("条件", "显示条件，如 courage>=3；不满足则置灰。语法错由校验器抓"), GUILayout.MinWidth(120));
                EditorGUILayout.PropertyField(setProp,
                    new GUIContent("赋值", "选中瞬间执行，分号分隔，如 courage+1; met_scholar=true"), GUILayout.MinWidth(120));
            }
        }

        if (choicesProp.arraySize == 0)
        {
            EditorGUILayout.LabelField("无选项（走『下一句』流转）", EditorStyles.miniLabel);
        }

        if (GUILayout.Button("＋ 添加选项", EditorStyles.miniButton, GUILayout.Width(90)))
        {
            choicesProp.arraySize++;
            var added = choicesProp.GetArrayElementAtIndex(choicesProp.arraySize - 1);
            added.FindPropertyRelative("text").stringValue = string.Empty;
            added.FindPropertyRelative("nextId").stringValue = string.Empty;
            added.FindPropertyRelative("condition").stringValue = string.Empty;
            added.FindPropertyRelative("setExpressions").stringValue = string.Empty;
        }

        EditorGUI.indentLevel--;
    }

    /// <summary>选项跳转目标下拉：列全部节点 id，空首项「（未设置）」，写值走 SerializedProperty。</summary>
    private void DrawChoiceTargetPopup(SerializedProperty nextIdProp, int total)
    {
        var labels = new List<string> { "（未设置）" };
        var values = new List<string> { string.Empty };

        for (int j = 0; j < total; j++)
        {
            var other = _nodes.GetArrayElementAtIndex(j);
            string otherId = other.FindPropertyRelative("id").stringValue;
            if (string.IsNullOrEmpty(otherId))
            {
                continue; // 无 id 的节点无法被跳转引用
            }

            labels.Add($"#{j + 1} · {otherId}");
            values.Add(otherId);
        }

        // 断链兜底：当前值非空但不在选项中（目标被删/改名）
        string current = nextIdProp.stringValue;
        if (!string.IsNullOrEmpty(current) && !values.Contains(current))
        {
            labels.Insert(1, $"⚠ 断链：{current}");
            values.Insert(1, current);
        }

        int selected = Mathf.Max(0, values.IndexOf(current));

        var options = labels.Select(l => new GUIContent(l)).ToArray();
        EditorGUI.BeginChangeCheck();
        int picked = EditorGUILayout.Popup(GUIContent.none, selected, options, GUILayout.MinWidth(70));
        if (EditorGUI.EndChangeCheck() && picked >= 0 && picked < values.Count)
        {
            nextIdProp.stringValue = values[picked];
        }
    }

    // ---------- 数组操作 ----------

    private void AddNode()
    {
        _nodes.arraySize++;
        var node = _nodes.GetArrayElementAtIndex(_nodes.arraySize - 1);
        string newId = GenerateUniqueId();
        node.FindPropertyRelative("id").stringValue = newId;
        node.FindPropertyRelative("speaker").objectReferenceValue = null; // 显式清引用，防脏数据
        node.FindPropertyRelative("speakerName").stringValue = string.Empty;
        node.FindPropertyRelative("text").stringValue = string.Empty;
        node.FindPropertyRelative("nextId").stringValue = string.Empty;
        _foldoutById[newId] = true; // 新节点默认展开方便直接填写
        serializedObject.ApplyModifiedProperties();
    }

    private void DeleteNode(int index)
    {
        // 不做引用自动清洗（静默改顺序 = 暗改语义）——删完校验立即标红，策划自己接
        _nodes.DeleteArrayElementAtIndex(index);
        serializedObject.ApplyModifiedProperties();
    }

    private void DuplicateNode(int index)
    {
        _nodes.InsertArrayElementAtIndex(index); // 复制到 index+1
        var copy = _nodes.GetArrayElementAtIndex(index + 1);
        string newId = GenerateUniqueId();
        copy.FindPropertyRelative("id").stringValue = newId; // 显式重写，不依赖插入实现细节
        _foldoutById[newId] = true;
        serializedObject.ApplyModifiedProperties();
    }

    private string GenerateUniqueId()
    {
        var existing = new HashSet<string>();
        for (int i = 0; i < _nodes.arraySize; i++)
        {
            existing.Add(_nodes.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue);
        }

        for (int n = 1; n < 10000; n++)
        {
            if (!existing.Contains($"n{n}"))
            {
                return $"n{n}";
            }
        }

        return System.Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    // ---------- 多选降级 ----------

    private void DrawMultiSelectionNotice()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox($"已选择 {targets.Length} 个对话资产。\n多选时仅展示校验结果，编辑节点请逐个选中。", MessageType.Info);

        foreach (var t in targets.OfType<DialogueAsset>())
        {
            var issues = DialogueValidator.Validate(t);
            int errors = issues.Count(i => i.Severity == DialogueIssueSeverity.Error);
            int warnings = issues.Count - errors;
            EditorGUILayout.LabelField($"{t.name}：{(issues.Count == 0 ? "✓ 通过" : $"错误 {errors} · 警告 {warnings}")}",
                issues.Count == 0 ? EditorStyles.boldLabel : IssueLabelStyle(errors > 0 ? DialogueIssueSeverity.Error : DialogueIssueSeverity.Warning));
        }
    }

    // ---------- 工具 ----------

    /// <summary>节点说话人的显示名：speaker 资产显示名 &gt; 旧文本名 &gt; "旁白"。</summary>
    private static string ResolveSpeakerDisplay(SerializedProperty nodeProp)
    {
        if (nodeProp.FindPropertyRelative("speaker").objectReferenceValue is SpeakerAsset speaker)
        {
            return speaker.GetDisplayName();
        }

        string legacy = nodeProp.FindPropertyRelative("speakerName").stringValue;
        return string.IsNullOrEmpty(legacy) ? "旁白" : legacy;
    }

    private static string CardKey(int index, string id)
    {
        return string.IsNullOrEmpty(id) ? $"#idx{index}" : id;
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "（空）";
        }

        return text.Length <= max ? text : text.Substring(0, max) + "…";
    }

    private static GUIStyle IssueLabelStyle(DialogueIssueSeverity severity)
    {
        var style = new GUIStyle(EditorStyles.label) { wordWrap = true };
        style.normal.textColor = severity == DialogueIssueSeverity.Error ? Color.red : new Color(0.9f, 0.55f, 0.1f);
        return style;
    }

    private static GUIStyle _errorBoxStyle;

    private static GUIStyle ErrorBoxStyle => _errorBoxStyle ??= new GUIStyle(GUI.skin.box)
    {
        normal = { textColor = Color.red },
    };
}
