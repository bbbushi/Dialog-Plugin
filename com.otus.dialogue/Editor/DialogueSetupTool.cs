using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 对话系统一键配置：中文字体 + 素材九宫格 border + UI 配置 + Demo 对话与 Cube 接线。
/// 菜单：Dialogue > Setup All
/// </summary>
public static class DialogueSetupTool
{
    private const string FontDir = "Assets/Fonts";
    private const string FontAssetPath = FontDir + "/SimHei SDF.asset";
    private const string ConfigPath = "Assets/Resources/DialogueUIConfig.asset";
    private const string DemoDir = "Assets/Dialogue";
    private const string DemoAssetPath = DemoDir + "/Demo Dialogue.asset";

    private const string PanelSpritePath =
        "Assets/Humble Gift - Paper UI System v1.1/Sprites/Paper UI Pack/Plain/4 Notification/1.png";
    private const string NameSpritePath =
        "Assets/Humble Gift - Paper UI System v1.1/Sprites/Paper UI Pack/Plain/2 Headers/1.png";
    private const string PortraitFrameSpritePath =
        "Assets/Humble Gift - Paper UI System v1.1/Sprites/Content/5 Holders/1.png";

    private const string SpeakersDir = DemoDir + "/Speakers";

    [MenuItem("Dialogue/Setup All")]
    public static void SetupAll()
    {
        // 1. TMP Essentials 前置检查（无 TMP Settings 则字体步骤无法落地）
        if (TMP_Settings.instance == null)
        {
            Debug.LogError(
                "[Dialogue Setup] 尚未导入 TMP Essentials。请先执行菜单 Window > TextMeshPro > Import TMP Essential Resources，完成后重跑 Dialogue > Setup All。");
            return;
        }

        TMP_FontAsset font = SetupChineseFont();
        SetupSpriteBorders();
        SetupConfig(font);
        var speakers = SetupSpeakers(); // 必须在 SetupDemo 之前：Demo 的 speaker 引用需要 GUID 先落盘
        SetupDemo(speakers);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        bool hasInteraction = Type.GetType("DialogueTrigger, Dialogue.Interaction") != null;
        bool hasVn = Type.GetType("DialogueDirector, Dialogue.Vn") != null;
        Debug.Log("[Dialogue Setup] 全部完成：中文字体、sprite border（含头像框）、UI 配置、Speaker 资产、Demo 对话与 Cube 接线。Demo 已含选项分支演示（n2 两选项）。进入 Play Mode 后点击 Cube 即可看到对话（左侧角色头像 + 无图首字缩写）。\n" +
                  "策划提示：① 选中 Demo Dialogue 用卡片编辑器修改，试试把「下一句」下拉改为跳转，再用 Dialogue > Open Preview 免 Play 预览；② 右键 Create > Dialogue > Speaker 新建角色（配头像/名字颜色）；③ 选中 DialogueUIConfig 资产可在样式编辑器里调字体/背景/颜色，每段对话可在 Inspector 里「创建新样式…」单独覆盖；"
                  + (hasInteraction
                      ? "④ 菜单 Dialogue > Trigger Manager 可视化管理场景对话触发器（靠近按 E 交谈 / 自动播放）。"
                      : "④ 需要场景触发器（靠近按 E 交谈）时安装 com.otus.dialogue.interaction 扩展包。")
                  + (hasVn
                      ? "⑤ 视觉小说全局串播用 Dialogue > Director Manager。"
                      : "⑤ 视觉小说章节串播（剧本导演）需安装 com.otus.dialogue.vn 扩展包。"));
    }

    /// <summary>复制系统字体并用 Dynamic 模式生成 TMP 字体资产（运行时按需补字，任意中文可显示）。</summary>
    private static TMP_FontAsset SetupChineseFont()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (existing != null)
        {
            ApplyDefaultFont(existing);
            return existing;
        }

        if (!Directory.Exists(FontDir))
        {
            Directory.CreateDirectory(FontDir);
        }

        // 源字体：优先 simhei.ttf（单 ttf 最稳），回退 msyh.ttc
        string source = "C:/Windows/Fonts/simhei.ttf";
        string dest = FontDir + "/simhei.ttf";
        if (!File.Exists(source))
        {
            source = "C:/Windows/Fonts/msyh.ttc";
            dest = FontDir + "/msyh.ttc";
        }

        if (!File.Exists(source))
        {
            Debug.LogError("[Dialogue Setup] 系统中找不到 simhei.ttf / msyh.ttc，请手动放置中文字体到 " + FontDir);
            return null;
        }

        File.Copy(source, dest, overwrite: true);
        AssetDatabase.ImportAsset(dest);

        var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(dest);
        if (sourceFont == null)
        {
            Debug.LogError($"[Dialogue Setup] 字体文件导入失败：{dest}（.ttc 偶发不兼容，可改用 .ttf）");
            return null;
        }

        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(
            sourceFont, 44, 9, GlyphRenderMode.SDFAA,
            2048, 2048, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);
        if (asset == null)
        {
            Debug.LogError("[Dialogue Setup] TMP_FontAsset 创建失败。");
            return null;
        }

        asset.name = "SimHei SDF";
        AssetDatabase.CreateAsset(asset, FontAssetPath);

        // 关键：CreateFontAsset 在内存中创建的 material/atlas 必须作为子对象存入资产，
        // 否则保存后引用丢失（font.material = null），TMP 无法生成任何网格
        asset.material.name = "SimHei SDF Material";
        AssetDatabase.AddObjectToAsset(asset.material, FontAssetPath);
        for (int i = 0; i < asset.atlasTextureCount; i++)
        {
            var atlas = asset.atlasTextures[i];
            if (atlas != null)
            {
                atlas.name = "SimHei SDF Atlas";
                AssetDatabase.AddObjectToAsset(atlas, FontAssetPath);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(FontAssetPath);
        ApplyDefaultFont(asset);
        Debug.Log($"[Dialogue Setup] 中文字体已生成：{FontAssetPath}（Dynamic 模式，缺字自动补录）");
        return asset;
    }

    private static void ApplyDefaultFont(TMP_FontAsset font)
    {
        if (font == null || TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == font)
        {
            return;
        }

        // TMP 3.0.6 中 defaultFontAsset 属性只读，需写序列化字段 m_defaultFontAsset
        var so = new SerializedObject(TMP_Settings.instance);
        so.FindProperty("m_defaultFontAsset").objectReferenceValue = font;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(TMP_Settings.instance);
        AssetDatabase.SaveAssets();
    }

    /// <summary>给纸质素材设置九宫格边框（Sliced 拉伸时保住边角与花纹）。</summary>
    private static void SetupSpriteBorders()
    {
        // 272x112：左边框取大以保住左侧装饰花纹；初值，运行目测后可微调
        SetBorder(PanelSpritePath, new Vector4(48, 28, 24, 24));
        // 608x176
        SetBorder(NameSpritePath, new Vector4(60, 40, 60, 40));
        // 224x224 纸质圆角方框 → 头像框
        SetBorder(PortraitFrameSpritePath, new Vector4(22, 22, 22, 22));
    }

    private static void SetBorder(string path, Vector4 border)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
        {
            Debug.LogWarning($"[Dialogue Setup] 找不到贴图，跳过 border 设置：{path}");
            return;
        }

        if (importer.spriteBorder == border)
        {
            return;
        }

        importer.spriteBorder = border;
        importer.SaveAndReimport();
        Debug.Log($"[Dialogue Setup] sprite border 已设置：{path} = ({border.x}, {border.y}, {border.z}, {border.w})");
    }

    private static void SetupConfig(TMP_FontAsset font)
    {
        var config = AssetDatabase.LoadAssetAtPath<DialogueUIConfig>(ConfigPath);
        if (config == null)
        {
            if (!Directory.Exists("Assets/Resources"))
            {
                Directory.CreateDirectory("Assets/Resources");
            }

            config = ScriptableObject.CreateInstance<DialogueUIConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
        }

        var so = new SerializedObject(config);
        so.FindProperty("font").objectReferenceValue = font;
        so.FindProperty("panelSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(PanelSpritePath);
        so.FindProperty("nameSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(NameSpritePath);
        so.FindProperty("portraitFrameSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(PortraitFrameSpritePath);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(config);
        Debug.Log($"[Dialogue Setup] UI 配置已生成/更新：{ConfigPath}");
    }

    /// <summary>创建 Demo 用 Speaker 资产（幂等）。必须在 SetupDemo 之前：GUID 先落盘，Demo 的引用才能解析。</summary>
    private static Dictionary<string, SpeakerAsset> SetupSpeakers()
    {
        if (!Directory.Exists(SpeakersDir))
        {
            Directory.CreateDirectory(SpeakersDir);
        }

        var map = new Dictionary<string, SpeakerAsset>();
        foreach (string speakerName in new[] { "旅人", "学者" })
        {
            string path = $"{SpeakersDir}/{speakerName}.asset";
            var speaker = AssetDatabase.LoadAssetAtPath<SpeakerAsset>(path);
            if (speaker == null)
            {
                speaker = ScriptableObject.CreateInstance<SpeakerAsset>();
                AssetDatabase.CreateAsset(speaker, path);
            }

            var so = new SerializedObject(speaker);
            so.FindProperty("displayName").stringValue = speakerName;
            // portrait 留空 → 运行时/预览显示名字首字缩写（无美术也能用）
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(speaker);
            map[speakerName] = speaker;
        }

        AssetDatabase.SaveAssets(); // GUID 落盘
        Debug.Log($"[Dialogue Setup] Speaker 资产就绪：{SpeakersDir}/（旅人、学者，无头像走首字缩写）");
        return map;
    }

    private static void SetupDemo(Dictionary<string, SpeakerAsset> speakers)
    {
        var demo = AssetDatabase.LoadAssetAtPath<DialogueAsset>(DemoAssetPath);
        if (demo == null)
        {
            if (!Directory.Exists(DemoDir))
            {
                Directory.CreateDirectory(DemoDir);
            }

            demo = ScriptableObject.CreateInstance<DialogueAsset>();
            AssetDatabase.CreateAsset(demo, DemoAssetPath);
        }

        // 文案特意含「」……，。！？——用于验证字体标点覆盖
        string[,] data =
        {
            { "n1", "旅人", "这里是……什么地方？" },
            { "n2", "学者", "欢迎来到测试用的对话系统。我是负责演示的学者。" },
            { "n3", "学者", "你现在看到的每一个字，都来自逐字显示的打字机效果。" },
            { "n4", "旅人", "点击鼠标左键，或者按下空格键，就可以立刻显示全部文字，或者推进到下一句。" },
            { "n5", "学者", "那么，演示到此结束。祝你的开发一切顺利！" },
        };

        var so = new SerializedObject(demo);
        var nodes = so.FindProperty("nodes");
        nodes.arraySize = data.GetLength(0);
        for (int i = 0; i < data.GetLength(0); i++)
        {
            var node = nodes.GetArrayElementAtIndex(i);
            node.FindPropertyRelative("id").stringValue = data[i, 0];
            node.FindPropertyRelative("speaker").objectReferenceValue =
                speakers.TryGetValue(data[i, 1], out var speaker) ? speaker : null; // 角色资产接管
            node.FindPropertyRelative("speakerName").stringValue = data[i, 1]; // 旧字段照旧写入（fallback 兼容演示）
            node.FindPropertyRelative("text").stringValue = data[i, 2];
            node.FindPropertyRelative("nextId").stringValue = string.Empty; // 全部留空 = 纯线性
            node.FindPropertyRelative("choices").ClearArray(); // 清残留选项：历史数据按 index 复位时旧的 choices 会串节点
            node.FindPropertyRelative("commands").ClearArray(); // 同理清残留进入命令
        }

        // n2 挂三条选项分支（ClearArray 幂等，重跑不叠加）；第三条演示"条件置灰"（永不满足）
        var n2Choices = nodes.GetArrayElementAtIndex(1).FindPropertyRelative("choices");
        n2Choices.ClearArray();
        AddChoice(n2Choices, "追问学者", "n3");
        AddChoice(n2Choices, "转身离开", "n4", setExpressions: "courage+1");
        AddChoice(n2Choices, "凝视深渊（演示置灰）", "n5", condition: "courage>=99");

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(demo);

        // 场景接线：给 Cube 挂触发器并赋值。触发器 2.0 起移入 interaction 扩展包——
        // core 不引用扩展程序集，按「类型名, 程序集名」探测，未装则跳过（不影响其余步骤）
        var demoTriggerType = Type.GetType("DialogueDemoTrigger, Dialogue.Interaction");
        if (demoTriggerType == null)
        {
            Debug.Log("[Dialogue Setup] 未安装 com.otus.dialogue.interaction 扩展包，跳过 Cube 触发器接线。"
                + "需要「点击/靠近按键」触发或视觉小说章节串播时，安装 interaction / vn 扩展包。");
            return;
        }

        GameObject cube = GameObject.Find("Cube");
        if (cube == null)
        {
            Debug.LogWarning("[Dialogue Setup] 当前场景找不到 Cube，请手动给目标物体挂 DialogueDemoTrigger 并拖入 Demo Dialogue。");
            return;
        }

        var trigger = cube.GetComponent(demoTriggerType) ?? cube.AddComponent(demoTriggerType);

        var triggerSo = new SerializedObject(trigger);
        triggerSo.FindProperty("dialogue").objectReferenceValue = demo;
        triggerSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(cube);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[Dialogue Setup] Demo 对话已生成：{DemoAssetPath}；Cube 已挂 DialogueDemoTrigger（记得保存场景 Ctrl+S）。");
    }

    private static void AddChoice(SerializedProperty choicesProp, string text, string nextId,
        string condition = null, string setExpressions = null)
    {
        choicesProp.arraySize++;
        var choice = choicesProp.GetArrayElementAtIndex(choicesProp.arraySize - 1);
        choice.FindPropertyRelative("text").stringValue = text;
        choice.FindPropertyRelative("nextId").stringValue = nextId;
        choice.FindPropertyRelative("condition").stringValue = condition ?? string.Empty;
        choice.FindPropertyRelative("setExpressions").stringValue = setExpressions ?? string.Empty;
    }
}
