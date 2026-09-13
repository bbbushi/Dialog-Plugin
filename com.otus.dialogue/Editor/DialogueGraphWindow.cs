using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView; // Node/Port/Edge/GraphView 在实验命名空间（2022.3 实际位置）
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// DialogueAsset 的节点图窗口（GraphView 最小闭环）：
/// 顶部 IMGUI 工具条（资产名/刷新/自适应视野）+ 下方画布；拖节点存坐标（Undo）、
/// 连线改 nextId/choices（全 SerializedObject 写入）、删边清引用、双击定位资产。
/// 重建策略：数据小，任何变更后全清重画，简单可靠（Undo 撤销也走同一入口）。
/// </summary>
public class DialogueGraphWindow : EditorWindow
{
    [SerializeField] private DialogueAsset _asset; // 序列化持有，域重载/进出 Play 后自动恢复
    private DialogueGraphView _graph;

    [MenuItem("Dialogue/Open Node Graph")]
    public static void OpenFromSelection()
    {
        Open(Selection.activeObject as DialogueAsset); // 未选中时开空窗口，画布给提示
    }

    /// <summary>Inspector「打开节点图」入口：打开窗口并载入指定资产。</summary>
    public static void Open(DialogueAsset asset)
    {
        var window = GetWindow<DialogueGraphWindow>("对话节点图");
        window.Load(asset);
    }

    private void Load(DialogueAsset asset)
    {
        _asset = asset;
        _graph?.Rebuild(_asset);
        Repaint();
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed += RebuildFromUndo;
        BuildLayout();
        _graph.Rebuild(_asset);
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= RebuildFromUndo;
    }

    private void RebuildFromUndo()
    {
        _graph?.Rebuild(_asset);
    }

    private void BuildLayout()
    {
        rootVisualElement.Clear();

        var toolbar = new IMGUIContainer(DrawToolbar);
        toolbar.style.height = 24;
        rootVisualElement.Add(toolbar);

        _graph = new DialogueGraphView(this);
        _graph.style.flexGrow = 1;
        rootVisualElement.Add(_graph);
    }

    private void DrawToolbar()
    {
        if (_graph == null)
        {
            return;
        }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label(_asset != null ? _asset.name : "（未选择对话资产）", EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(_asset == null))
            {
                if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(44)))
                {
                    _graph.Rebuild(_asset);
                }

                if (GUILayout.Button("自适应视野", EditorStyles.toolbarButton, GUILayout.Width(74)))
                {
                    _graph.schedule.Execute(() => _graph.FrameAll()); // 延一帧等布局生效
                }
            }
        }
    }

    /// <summary>把画布上所有节点坐标写回资产布局（有位移才记 Undo/脏标记，批量一次）。</summary>
    public void SaveNodePositions()
    {
        if (_asset == null || _graph == null)
        {
            return;
        }

        bool changed = false;
        foreach (var view in _graph.NodeViews)
        {
            if (string.IsNullOrEmpty(view.NodeId))
            {
                continue; // 无 id 节点不落布局
            }

            // GetPosition 单一数据源（含拖动后 style 与测量布局的合成），双源读取会在重排后拿到旧值
            var pos = view.GetPosition();
            float x = pos.x;
            float y = pos.y;

            var stored = _asset.GetLayout(view.NodeId);
            if (stored != null && Mathf.Abs(stored.X - x) < 0.5f && Mathf.Abs(stored.Y - y) < 0.5f)
            {
                continue; // 未动
            }

            if (!changed)
            {
                Undo.RecordObject(_asset, "挪节点");
                changed = true;
            }

            _asset.SetLayout(view.NodeId, x, y);
        }

        if (changed)
        {
            EditorUtility.SetDirty(_asset);
        }
    }

    /// <summary>删除一个玩家选项本体（区别于删边只清 nextId）——按节点 id 定位后删 choices[i]，带 Undo。</summary>
    private void DeleteChoice(PortRef source)
    {
        if (_asset == null || source.ChoiceIndex < 0)
        {
            return;
        }

        var so = new SerializedObject(_asset);
        var nodes = so.FindProperty("nodes");
        for (int i = 0; i < nodes.arraySize; i++)
        {
            var candidate = nodes.GetArrayElementAtIndex(i);
            if (candidate.FindPropertyRelative("id").stringValue != source.NodeId)
            {
                continue;
            }

            var choices = candidate.FindPropertyRelative("choices");
            if (source.ChoiceIndex < choices.arraySize)
            {
                Undo.RecordObject(_asset, "删选项");
                choices.DeleteArrayElementAtIndex(source.ChoiceIndex);
                so.ApplyModifiedProperties();
            }

            break;
        }

        _graph.schedule.Execute(() => _graph.Rebuild(_asset)); // 延一帧重建（删空后节点回主出口流转）
    }

    /// <summary>出口端口的数据定位：按节点 id（而非下标——节点列表在 Inspector 增删后下标会漂移）+ 选项索引（-1 = 主出口）。</summary>
    private readonly struct PortRef
    {
        public readonly string NodeId;
        public readonly int ChoiceIndex;

        public PortRef(string nodeId, int choiceIndex)
        {
            NodeId = nodeId;
            ChoiceIndex = choiceIndex;
        }
    }

    /// <summary>图内节点：保留 id 与端口引用，供建边与连线写入定位。</summary>
    private class NodeView : Node
    {
        public string NodeId;
        public Port InputPort;
        public Port MainPort;
        public readonly List<Port> ChoicePorts = new List<Port>();
    }

    private class DialogueGraphView : GraphView
    {
        private readonly DialogueGraphWindow _window;
        private DialogueAsset _asset;
        private Label _hint; // 空资产提示（非 GraphElement，需单独清除）
        private bool m_Panning; // 左键拖空白平移中
        private Vector2 m_PanStartMouse;
        private Vector3 m_PanStartView;

        /// <summary>按下处的祖先链上是否挂着一个 GraphElement（节点标题的 Label 不是 GraphElement，必须向上遍历）。</summary>
        private static bool HitsGraphElement(VisualElement ve)
        {
            while (ve != null)
            {
                if (ve is GraphElement)
                {
                    return true;
                }

                ve = ve.parent;
            }

            return false;
        }

        public DialogueGraphView(DialogueGraphWindow window)
        {
            _window = window;

            SetupZoom(0.1f, 3f);
            this.AddManipulator(new SelectionDragger()); // 拖节点（拖空白时它自己不激活）
            // 左键拖空白平移视角：不用 ContentDragger——换左键激活后与节点拖动抢事件
            // （节点标题的 Label 不是 GraphElement，会被误判成空白）。自研判定：按下处
            // 向上遍历祖先不属于任何 GraphElement 才启动平移
            RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button != (int)MouseButton.LeftMouse || HitsGraphElement(e.target as VisualElement))
                {
                    return;
                }

                ClearSelection(); // 点空白清选区
                m_Panning = true;
                m_PanStartMouse = e.mousePosition;
                m_PanStartView = viewTransform.position;
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            RegisterCallback<MouseMoveEvent>(e =>
            {
                if (!m_Panning)
                {
                    return;
                }

                var delta = (Vector2)e.mousePosition - m_PanStartMouse;
                float zoom = viewTransform.scale.x;
                var newPos = m_PanStartView + new Vector3(delta.x / zoom, delta.y / zoom, 0f);
                UpdateViewTransform(newPos, viewTransform.scale);
            }, TrickleDown.TrickleDown);
            RegisterCallback<MouseUpEvent>(e => m_Panning = false, TrickleDown.TrickleDown);

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            // 拖动中指针被 GraphView 捕获，MouseUp 仍派发到捕获者：在这里统一落坐标兜底
            RegisterCallback<MouseUpEvent>(_ => _window.SaveNodePositions());

            graphViewChanged += OnGraphViewChanged;
        }

        /// <summary>画布上所有节点视图（存坐标遍历用）。</summary>
        public IEnumerable<NodeView> NodeViews => graphElements.OfType<NodeView>();

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var result = new List<Port>();
            foreach (var port in ports.ToList())
            {
                if (port.direction == startPort.direction)
                {
                    continue; // 只允许出→入
                }

                if (port.node == startPort.node)
                {
                    continue; // 禁自环：自己跳自己 = 死循环叙事
                }

                if (port.direction == Direction.Input && string.IsNullOrEmpty(port.userData as string))
                {
                    continue; // 无 id 节点不可被引用
                }

                result.Add(port);
            }

            return result;
        }

        /// <summary>全清重画：节点（瀑布默认/存档布局）+ 边（数据驱动，断链出口红框警示）。</summary>
        public void Rebuild(DialogueAsset asset)
        {
            _asset = asset;

            // RemoveElement 正确注销 graphElements（RemoveFromHierarchy 只摘视觉，注册残留会导致
            // 重建后选择/连线/缩放跟踪错乱）；它不触发 graphViewChanged，重建不会误写数据
            foreach (var element in graphElements.ToList())
            {
                RemoveElement(element);
            }

            if (_hint != null)
            {
                _hint.RemoveFromHierarchy();
                _hint = null;
            }

            if (asset == null)
            {
                _hint = new Label("未选择对话资产：在 Project 里选中一个 Dialogue Asset，\n或在其 Inspector 点「打开节点图」。");
                _hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                _hint.StretchToParentSize();
                Add(_hint);
                return;
            }

            var nodeViews = new List<NodeView>();
            var viewById = new Dictionary<string, NodeView>();
            for (int i = 0; i < asset.Nodes.Count; i++)
            {
                var view = BuildNodeView(asset, i); // 只做骨架：title + 坐标 + 端口
                nodeViews.Add(view);
                AddElement(view); // 必须先挂树再打扮——"扩展容器+RefreshExpandedState"等样式操作
                DressNodeView(view, asset.Nodes[i]); // 在 AddElement 前做会令端口布局失效（NaN，视觉与拾取全坏）
                if (!string.IsNullOrEmpty(view.NodeId))
                {
                    viewById[view.NodeId] = view;
                }
            }

            for (int i = 0; i < nodeViews.Count; i++)
            {
                var node = asset.Nodes[i];
                var view = nodeViews[i];
                var choices = node.Choices;
                int choiceCount = choices != null ? choices.Count : 0;

                if (!node.HasChoices)
                {
                    string targetId = node.NextId;
                    if (string.IsNullOrEmpty(targetId))
                    {
                        if (i < nodeViews.Count - 1)
                        {
                            Connect(view.MainPort, nodeViews[i + 1].InputPort, implicitEdge: true); // 顺序流转（与运行时 GetNext 同语义）
                        }
                    }
                    else if (viewById.TryGetValue(targetId, out var nextView))
                    {
                        Connect(view.MainPort, nextView.InputPort);
                    }
                    else
                    {
                        MarkBroken(view.MainPort); // 断链警示（不建边，Validator 会标红）
                    }
                }
                else
                {
                    for (int c = 0; c < choiceCount; c++)
                    {
                        string targetId = choices[c].NextId;
                        if (string.IsNullOrEmpty(targetId))
                        {
                            continue;
                        }

                        if (viewById.TryGetValue(targetId, out var targetView))
                        {
                            Connect(view.ChoicePorts[c], targetView.InputPort);
                        }
                        else
                        {
                            MarkBroken(view.ChoicePorts[c]);
                        }
                    }
                }
            }
        }

        /// <summary>骨架构建：仅 title/坐标/端口（端口结构必须在挂树前就位，样式打扮一律推迟到 DressNodeView）。</summary>
        private NodeView BuildNodeView(DialogueAsset asset, int index)
        {
            var node = asset.Nodes[index];
            string speaker = string.IsNullOrEmpty(node.ResolvedSpeakerName) ? "旁白" : node.ResolvedSpeakerName;
            var view = new NodeView
            {
                NodeId = node.Id,
                title = $"{(string.IsNullOrEmpty(node.Id) ? "?" : node.Id)}（{speaker}）",
            };

            var layout = asset.GetLayout(node.Id);
            float x = layout != null ? layout.X : index % 3 * 320f + 60f; // 无记录用瀑布默认
            float y = layout != null ? layout.Y : index / 3 * 220f + 40f;
            view.SetPosition(new Rect(x, y, 0, 0)); // GraphElement.SetPosition(2022.3) 只落 left/top

            var input = Port.Create<Edge>(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            input.portName = "入口";
            input.userData = view.NodeId; // 入口携带目标节点 id，连线写入时读取
            view.inputContainer.Add(input);
            view.InputPort = input;

            var main = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
            main.portName = "下一句";
            main.userData = new PortRef(node.Id ?? "", -1);
            view.outputContainer.Add(main);
            view.MainPort = main;

            var choices = node.Choices;
            int choiceCount = choices != null ? choices.Count : 0;
            for (int c = 0; c < choiceCount; c++)
            {
                var choicePort = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
                choicePort.portName = string.IsNullOrEmpty(choices[c].Text) ? "(空文案)" : choices[c].Text;
                choicePort.userData = new PortRef(node.Id ?? "", c);
                // 图上删选项本体：删边只清 nextId，选项还在——节点会一直停在「选项接管」，
                // 右键端口是图内退出接管的唯一入口（卡片编辑器里有「清空选项」）
                choicePort.AddManipulator(new ContextualMenuManipulator(evt =>
                {
                    evt.menu.AppendAction("删除该选项（退出接管）", _ => _window.DeleteChoice(new PortRef(node.Id ?? "", c)));
                }));
                view.outputContainer.Add(choicePort);
                view.ChoicePorts.Add(choicePort);
            }

            return view;
        }

        /// <summary>挂在树上之后才能做的打扮：定宽/摘要/展开/禁折叠/主出口灰化/双击。顺序错误会让端口布局 NaN（视觉与连线拾取全坏）。</summary>
        private void DressNodeView(NodeView view, DialogueNode node)
        {
            view.style.width = 300f; // 定宽保证版面整齐，高度交给内容自适应

            var body = new Label(PreviewText(node.Text));
            body.style.whiteSpace = WhiteSpace.Normal; // 折行成 ~2 行摘要
            body.style.fontSize = 11;
            body.style.paddingLeft = 6;
            body.style.paddingRight = 6;
            view.extensionContainer.Add(body);
            view.expanded = true;
            view.RefreshExpandedState();

            // 内容固定（摘要+端口），折叠无意义；折叠/展开触发节点树重排，实验版 GraphView
            // 的边跟踪在该场景下不可靠（展开后连线错位的来源），直接禁用
            var collapse = view.titleContainer.Q("collapse-button");
            if (collapse != null)
            {
                collapse.style.display = DisplayStyle.None;
            }

            if (node.HasChoices)
            {
                view.MainPort.style.opacity = 0.35f; // 灰化：选项接管后主出口不生效
                view.MainPort.tooltip = "被选项接管";
            }

            // TrickleDown：节点自身先于 manipulator 收到，位置已是拖动结果；双击定位资产
            view.RegisterCallback<MouseUpEvent>(e =>
            {
                _window.SaveNodePositions();
                if (e.clickCount == 2)
                {
                    EditorGUIUtility.PingObject(_asset);
                    Selection.activeObject = _asset;
                }
            }, TrickleDown.TrickleDown);
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange changes)
        {
            bool dataChanged = false;

            // 先删后建：Capacity.Single 出口换线时旧边清空、新边覆盖，顺序保证结果正确
            if (changes.elementsToRemove != null)
            {
                foreach (var edge in changes.elementsToRemove.OfType<Edge>())
                {
                    dataChanged |= WriteTarget(edge, string.Empty);
                }
            }

            if (changes.edgesToCreate != null)
            {
                foreach (var edge in changes.edgesToCreate)
                {
                    dataChanged |= WriteTarget(edge, edge.input?.userData as string);
                }
            }

            // Delete 键只允许删边（写空引用）；删节点的请求一律在下方 Rebuild 中"复原"——
            // 节点增删走 Inspector 卡片，图上误按 Delete 直接删数据节点的代价太大（实测删过 n1）
            bool anyRemoved = changes.elementsToRemove != null && changes.elementsToRemove.Count > 0;
            if (dataChanged || anyRemoved)
            {
                schedule.Execute(() => Rebuild(_asset)); // 延一帧：回调内清元素不安全；重建顺带去重防重复边
            }

            return changes;
        }

        /// <summary>把某出口的目标写进 SerializedProperty（nodes[i].nextId 或 nodes[i].choices[j].nextId）；写成功返回 true。</summary>
        private bool WriteTarget(Edge edge, string targetId)
        {
            if (_asset == null || !(edge.output?.userData is PortRef source))
            {
                return false;
            }

            var so = new SerializedObject(_asset);
            var prop = ResolveTargetProp(so, source);
            if (prop == null || prop.stringValue == targetId)
            {
                return false;
            }

            Undo.RecordObject(_asset, "改连线"); // 必须在改值前快照，否则 SerializedObject 写入不入撤销栈
            prop.stringValue = targetId;
            so.ApplyModifiedProperties();
            return true;
        }

        private static SerializedProperty ResolveTargetProp(SerializedObject so, PortRef source)
        {
            var nodes = so.FindProperty("nodes");
            SerializedProperty node = null;
            for (int i = 0; i < nodes.arraySize; i++)
            {
                var candidate = nodes.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative("id").stringValue == source.NodeId)
                {
                    node = candidate;
                    break;
                }
            }

            if (node == null)
            {
                return null;
            }

            if (source.ChoiceIndex < 0)
            {
                return node.FindPropertyRelative("nextId");
            }

            var choices = node.FindPropertyRelative("choices");
            if (source.ChoiceIndex >= choices.arraySize)
            {
                return null;
            }

            return choices.GetArrayElementAtIndex(source.ChoiceIndex).FindPropertyRelative("nextId");
        }

        private void Connect(Port output, Port input, bool implicitEdge = false)
        {
            if (output == null || input == null)
            {
                return;
            }

            var edge = new Edge { output = output, input = input };
            if (implicitEdge)
            {
                edge.userData = "implicit";
                // 顺序流转是数据隐含语义（nextId 空 = 顺序播放），没有可清的引用——删了会被重建。
                // 直接去掉 Deletable 能力，Delete 键/右键删除对它无效
                edge.capabilities &= ~Capabilities.Deletable;
                edge.style.opacity = 0.55f; // 与显式连线区分：数据隐含，不可删
                edge.tooltip = "顺序流转（nextId 为空 = 顺序播放，不可断开）。要改变去向，请在 Inspector 编辑上一节点的『下一句』";
            }

            output.Connect(edge);
            input.Connect(edge);
            AddElement(edge); // 必须注册进 graphElements，否则缩放时边与端口分层脱节
        }

        private static void MarkBroken(Port port)
        {
            port.style.borderTopColor = Color.red;
            port.style.borderBottomColor = Color.red;
            port.style.borderLeftColor = Color.red;
            port.style.borderRightColor = Color.red;
            port.style.borderTopWidth = 1.5f;
            port.style.borderBottomWidth = 1.5f;
            port.style.borderLeftWidth = 1.5f;
            port.style.borderRightWidth = 1.5f;
            port.tooltip = "跳转目标不存在（断链）";
        }

        private static string PreviewText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "（空正文）";
            }

            string flat = text.Replace("\n", " ");
            return flat.Length <= 40 ? flat : flat.Substring(0, 40) + "…";
        }
    }
}
