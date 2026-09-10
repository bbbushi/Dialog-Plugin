using System;
using UnityEngine;

/// <summary>
/// 玩家选项：一句话说完后可点选的分支入口。
/// 纯数据，零依赖；nextId 指向目标节点 id（断链由 Validator 在编辑期抓）。
/// condition/setExpressions 语法见 DialogueExpression（空 = 无条件/无副作用）。
/// </summary>
[Serializable]
public class DialogueChoice
{
    [SerializeField] private string text;

    [SerializeField] private string nextId;

    [SerializeField] private string condition; // 显示条件，如 "courage>=3"；不满足则置灰

    [SerializeField] private string setExpressions; // 选中瞬间执行的赋值，分号分隔，如 "courage+1; met_scholar=true"

    public string Text => text;

    public string NextId => nextId;

    public string Condition => condition;

    public string SetExpressions => setExpressions;
}
