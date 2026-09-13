using TMPro;
using UnityEngine;

/// <summary>
/// 对话 UI 配置：字体、贴图、颜色、语速集中于此（Resources/DialogueUIConfig）。
/// 换皮只改这份配置，不动代码。
/// </summary>
[CreateAssetMenu(fileName = "DialogueUIConfig", menuName = "Dialogue/UI Config")]
public class DialogueUIConfig : ScriptableObject
{
    [SerializeField] private TMP_FontAsset font;

    [SerializeField] private Sprite panelSprite;

    [SerializeField] private Sprite nameSprite;

    [SerializeField] private Sprite portraitFrameSprite; // 头像框（九宫格；空 → UI 兜底色块）

    [SerializeField] private Color textColor = new Color32(0x3E, 0x2F, 0x23, 0xFF); // 深棕（纸面墨色）

    [SerializeField] private Color speakerColor = new Color32(0x7A, 0x3B, 0x2E, 0xFF); // 红棕

    [SerializeField] private Color choiceColor = new Color32(0xF5, 0xE9, 0xD0, 0xF2); // 选项按钮底色（淡纸色半透明）

    [SerializeField] private Color choiceTextColor = new Color32(0x5A, 0x46, 0x32, 0xFF); // 选项文字（中棕）

    [SerializeField, Min(1f)] private float charactersPerSecond = 30f;

    [SerializeField, Min(0.1f)] private float autoAdvanceDelay = 1.5f; // 自动播放：显示完到自动推进的间隔（秒）

    [SerializeField, Min(1f)] private float readSpeedMultiplier = 3f; // 已读节点打字加速倍率

    [Tooltip("布局预制体覆盖（空 = 代码默认布局）。用菜单 Dialogue > UI > 导出当前 UI 为预制体 生成起点后随意改布局/装饰；缺必需节点会自动回退默认并报错")]
    [SerializeField] private GameObject layoutPrefab;

    public TMP_FontAsset Font => font;

    public Sprite PanelSprite => panelSprite;

    public Sprite NameSprite => nameSprite;

    public Sprite PortraitFrameSprite => portraitFrameSprite;

    public Color TextColor => textColor;

    public Color SpeakerColor => speakerColor;

    public Color ChoiceColor => choiceColor;

    public Color ChoiceTextColor => choiceTextColor;

    public float CharactersPerSecond => charactersPerSecond;

    public float AutoAdvanceDelay => autoAdvanceDelay;

    public float ReadSpeedMultiplier => readSpeedMultiplier;

    public GameObject LayoutPrefab => layoutPrefab;
}
