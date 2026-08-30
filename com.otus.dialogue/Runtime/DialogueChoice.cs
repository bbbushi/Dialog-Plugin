using System;
using UnityEngine;

/// <summary>
/// 玩家选项：一句话说完后可点选的分支入口。
/// 纯数据，零依赖；nextId 指向目标节点 id（断链由 Validator 在编辑期抓）。
/// </summary>
[Serializable]
public class DialogueChoice
{
    [SerializeField] private string text;

    [SerializeField] private string nextId;

    public string Text => text;

    public string NextId => nextId;
}
