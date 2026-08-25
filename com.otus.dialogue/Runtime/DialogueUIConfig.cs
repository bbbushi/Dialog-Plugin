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

    [SerializeField, Min(1f)] private float charactersPerSecond = 30f;

    public TMP_FontAsset Font => font;

    public Sprite PanelSprite => panelSprite;

    public Sprite NameSprite => nameSprite;

    public Sprite PortraitFrameSprite => portraitFrameSprite;

    public Color TextColor => textColor;

    public Color SpeakerColor => speakerColor;

    public float CharactersPerSecond => charactersPerSecond;
}
