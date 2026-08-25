using UnityEngine;

/// <summary>
/// 对话角色资产：显示名、头像、可选名字颜色覆盖。
/// 纯数据零依赖（不反向引用 DialogueNode），可被角色系统复用。
/// </summary>
[CreateAssetMenu(fileName = "NewSpeaker", menuName = "Dialogue/Speaker")]
public class SpeakerAsset : ScriptableObject
{
    [SerializeField] private string displayName;

    [SerializeField] private Sprite portrait; // 空 → UI 显示名字首字缩写

    // bool+Color 双字段表达"颜色覆盖"：Unity Color 不可空，alpha 哨兵是坑（手滑调 alpha 触发回退，语义反直觉）
    [SerializeField] private bool overrideNameColor;

    [SerializeField] private Color nameColor = new Color32(0x7A, 0x3B, 0x2E, 0xFF);

    public Sprite Portrait => portrait;

    /// <summary>显示名：非空白 displayName &gt; 资产名。不变量：返回值永不为空（资产文件名非空）。</summary>
    public string GetDisplayName()
    {
        return string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    }

    /// <summary>名字颜色：勾选覆盖则用 nameColor，否则用调用方给的回退色（如样式里的 SpeakerColor）。</summary>
    public Color ResolveNameColor(Color fallback)
    {
        return overrideNameColor ? nameColor : fallback;
    }
}
