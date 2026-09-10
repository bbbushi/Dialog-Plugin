using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 对话存档：槽位式 JsonUtility 持久化（persistentDataPath/DialogueSaves/slot_N.json）。
/// 只做文件 IO，快照内容契约由 DialogueSnapshot（DialogueManager.cs）定义。
/// </summary>
public static class DialogueSaveSystem
{
    private const string DirName = "DialogueSaves";

    public static void Save(int slot, DialogueSnapshot snap)
    {
        if (snap == null)
        {
            throw new ArgumentNullException(nameof(snap));
        }

        if (slot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slot)); // 负槽位属调用方 bug，宁可早炸
        }

        Directory.CreateDirectory(GetDir()); // 已存在时静默跳过
        File.WriteAllText(GetPath(slot), JsonUtility.ToJson(snap, false));
    }

    /// <summary>读档：无文件/内容损坏一律 false + LogWarning，异常不外泄（存档坏了不该崩游戏）。</summary>
    public static bool TryLoad(int slot, out DialogueSnapshot snap)
    {
        snap = null;
        string path = GetPath(slot);
        if (slot < 0 || !File.Exists(path))
        {
            return false;
        }

        try
        {
            snap = JsonUtility.FromJson<DialogueSnapshot>(File.ReadAllText(path));
            return snap != null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DialogueSaveSystem] 槽位 {slot} 解析失败：{e.Message}");
            snap = null;
            return false;
        }
    }

    public static bool Delete(int slot)
    {
        if (slot < 0)
        {
            return false;
        }

        string path = GetPath(slot);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    /// <summary>扫目录取所有可解析槽位号（升序）；坏文件静默剔除（TryLoad 已告警）。</summary>
    public static IReadOnlyList<int> ListSlots()
    {
        var slots = new List<int>();
        string dir = GetDir();
        if (!Directory.Exists(dir))
        {
            return slots;
        }

        foreach (string file in Directory.GetFiles(dir, "slot_*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name.Length > 5
                && name.StartsWith("slot_", StringComparison.Ordinal)
                && int.TryParse(name.Substring(5), out int slot)
                && slot >= 0
                && TryLoad(slot, out _))
            {
                slots.Add(slot);
            }
        }

        slots.Sort();
        return slots;
    }

    private static string GetDir()
    {
        return Path.Combine(Application.persistentDataPath, DirName);
    }

    private static string GetPath(int slot)
    {
        return Path.Combine(GetDir(), $"slot_{slot}.json");
    }
}
