using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 剧本导演：视觉小说的全局入口。按顺序播放章节列表，章末自动串章，
/// 每章可配变量条件（不满足自动跳过——同一位置放多条不同条件的章即可做分歧结局）。
/// 条件语法与选项条件同源（DialogueExpression，如 courage>=5）；会话变量跨章保留。
/// 假定 Director 是唯一对话驱动（视觉小说用法）；混用触发器时请在无对话播放时手动调 Play()。
/// </summary>
[AddComponentMenu("Dialogue/Dialogue Director")]
public class DialogueDirector : MonoBehaviour
{
    /// <summary>一章 = 对话资产 + 可选进入条件（空 = 无条件）。</summary>
    [Serializable]
    public class Chapter
    {
        [SerializeField] private DialogueAsset asset;

        [Tooltip("进入条件（空 = 无条件），语法同选项条件：courage>=5")]
        [SerializeField] private string condition;

        public Chapter()
        {
        }

        public Chapter(DialogueAsset asset, string condition = null)
        {
            this.asset = asset;
            this.condition = condition;
        }

        public DialogueAsset Asset => asset;

        public string Condition => condition;
    }

    [Tooltip("章节列表：从上到下顺序播放；条件不满足或空资产的章自动跳过")]
    [SerializeField] private List<Chapter> chapters = new List<Chapter>();

    [Tooltip("勾选后场景加载即从第一个可播章开播（视觉小说全局入口）")]
    [SerializeField] private bool playOnStart = true;

    /// <summary>全部章节播完（或全被条件跳过）时触发一次。接主菜单/制作人员名单：订阅它。</summary>
    public event Action Finished;

    /// <summary>总章数（管理器窗口显示用）。</summary>
    public int ChapterCount => chapters.Count;

    private int _index = -1;  // 当前章索引（-1 = 未开播/已收尾）
    private bool _running;

    /// <summary>当前章资产（未开播/播完为 null）。</summary>
    public DialogueAsset CurrentChapter =>
        _index >= 0 && _index < chapters.Count ? chapters[_index].Asset : null;

    private void Start()
    {
        if (playOnStart)
        {
            Play();
        }
    }

    /// <summary>从头开播（已在播则忽略）。会话变量/历史跨章保留——分歧条件靠它们积累。</summary>
    public void Play()
    {
        if (_running)
        {
            return;
        }

        if (DialogueManager.IsAnyPlaying)
        {
            Debug.LogWarning("[Dialogue] 已有对话在播放，Director 不开播。视觉小说模式下请勿与其他触发器混用；手动调用请在对话结束后。", this);
            return;
        }

        _running = true;
        _index = 0;

        var manager = DialogueManager.EnsureInstance();
        manager.DialogueEnded -= OnDialogueEnded; // 防重复订阅
        manager.DialogueEnded += OnDialogueEnded;

        EnterCurrentOrFinish();
    }

    /// <summary>进入当前可播章；没有可播章则收尾。</summary>
    private void EnterCurrentOrFinish()
    {
        _index = FindNextPlayableIndex(chapters, _index, DialogueManager.Instance.Variables);

        if (_index < 0)
        {
            Finish();
            return;
        }

        DialogueManager.Instance.StartDialogue(chapters[_index].Asset);
    }

    /// <summary>章末推进：只认自己当前章的结束（别的对话结束不推进，防串台）。</summary>
    private void OnDialogueEnded(DialogueAsset ended)
    {
        if (!_running || !ReferenceEquals(ended, CurrentChapter))
        {
            return;
        }

        _index++;
        EnterCurrentOrFinish();
    }

    private void Finish()
    {
        _running = false;
        _index = -1;
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.DialogueEnded -= OnDialogueEnded;
        }

        Debug.Log("[Dialogue] 剧本播放完毕（全部章节结束或被条件跳过）。订阅 DialogueDirector.Finished 可接主菜单/制作名单。");
        Finished?.Invoke();
    }

    private void OnDisable()
    {
        // 场景卸载/物体失活时解绑防泄漏；重新启用后调 Play() 可从头再播
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.DialogueEnded -= OnDialogueEnded;
        }

        _running = false;
    }

    /// <summary>
    /// 从 from（含）起找第一个可播章：资产非空且有节点、条件满足；无则 -1。
    /// 条件语法错按无条件播放并报错（对齐运行时选项的兜底惯例：编辑期没抓住也不卡死流程）。
    /// </summary>
    public static int FindNextPlayableIndex(IReadOnlyList<Chapter> list, int from, DialogueVariables vars)
    {
        if (list == null)
        {
            return -1;
        }

        for (int i = Mathf.Max(from, 0); i < list.Count; i++)
        {
            var chapter = list[i];
            if (chapter == null || chapter.Asset == null || chapter.Asset.Nodes.Count == 0)
            {
                continue; // 空章（未接线/无节点）直接跳过
            }

            try
            {
                if (DialogueExpression.Evaluate(vars, chapter.Condition))
                {
                    return i;
                }
            }
            catch (FormatException e)
            {
                Debug.LogError($"[Dialogue] 章节 #{i + 1} 条件解析失败：{e.Message}——按无条件播放。");
                return i;
            }
        }

        return -1;
    }
}
