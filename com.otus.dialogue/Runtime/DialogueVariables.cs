using System;
using System.Collections.Generic;

/// <summary>
/// 对话变量存储：bool/int 统一按 int 存（true=1 / false=0）。
/// 实例归 Manager 持有（非静态——读档/重开对话需要重置）；未定义变量读取返回 0（动态语义，商业对话系统同惯例）。
/// </summary>
public class DialogueVariables
{
    private readonly Dictionary<string, int> _values = new Dictionary<string, int>();

    /// <summary>值真正变化时触发（角标/调试面板刷新用）。</summary>
    public event Action Changed;

    public int Get(string name)
    {
        return _values.TryGetValue(name, out var value) ? value : 0;
    }

    public void Set(string name, int value)
    {
        if (_values.TryGetValue(name, out var old) && old == value)
        {
            return; // 同值写入不触发事件
        }

        _values[name] = value;
        Changed?.Invoke();
    }

    /// <summary>快照导出（读档/调试）。</summary>
    public IReadOnlyDictionary<string, int> GetAll() => _values;

    /// <summary>读档导入（整体替换）。</summary>
    public void Import(IReadOnlyDictionary<string, int> src)
    {
        _values.Clear();
        if (src != null)
        {
            foreach (var kv in src)
            {
                _values[kv.Key] = kv.Value;
            }
        }

        Changed?.Invoke();
    }
}

/// <summary>
/// 极简表达式（手写解析，零依赖）：
/// 条件 "name op value"，op ∈ == != &gt;= &lt;= &gt; &lt;，value ∈ true/false/整数；
/// 赋值 "name=value" / "name+n" / "name-n"，多条赋值分号分隔。
/// 解析失败 throw FormatException——编辑期 Validator 抓，运行时 Manager catch 后按无条件处理。
/// </summary>
public static class DialogueExpression
{
    private static readonly string[] Operators = { "==", "!=", ">=", "<=", ">", "<" };

    public static bool Evaluate(DialogueVariables vars, string condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        string text = condition.Trim();
        int opPos = -1;
        string op = null;
        foreach (var candidate in Operators)
        {
            int idx = text.IndexOf(candidate, StringComparison.Ordinal);
            if (idx > 0 && (opPos < 0 || idx < opPos))
            {
                opPos = idx;
                op = candidate;
            }
        }

        if (op == null)
        {
            throw new FormatException($"条件缺少比较运算符（== != >= <= > <）：\"{condition}\"");
        }

        string name = text.Substring(0, opPos).Trim();
        string valueText = text.Substring(opPos + op.Length).Trim();
        if (name.Length == 0 || valueText.Length == 0)
        {
            throw new FormatException($"条件的变量名或值缺失：\"{condition}\"");
        }

        int left = vars.Get(name);
        int right = ParseValue(valueText, condition);
        switch (op)
        {
            case "==": return left == right;
            case "!=": return left != right;
            case ">=": return left >= right;
            case "<=": return left <= right;
            case ">": return left > right;
            default: return left < right;
        }
    }

    /// <summary>执行赋值串（分号分隔逐条；单条失败整体 throw——原子性由调用方 catch 决定）。</summary>
    public static void Apply(DialogueVariables vars, string setExpressions)
    {
        if (string.IsNullOrWhiteSpace(setExpressions))
        {
            return;
        }

        foreach (var raw in setExpressions.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            ApplyOne(vars, raw.Trim(), setExpressions);
        }
    }

    private static void ApplyOne(DialogueVariables vars, string text, string full)
    {
        int idx = text.IndexOf('=');
        if (idx > 0 && idx < text.Length - 1 && !IsComparePosition(text, idx))
        {
            string name = text.Substring(0, idx).Trim();
            int value = ParseValue(text.Substring(idx + 1).Trim(), full);
            vars.Set(name, value);
            return;
        }

        idx = text.IndexOf('+');
        if (idx > 0 && idx < text.Length - 1)
        {
            string name = text.Substring(0, idx).Trim();
            int delta = ParseValue(text.Substring(idx + 1).Trim(), full);
            vars.Set(name, vars.Get(name) + delta);
            return;
        }

        idx = text.IndexOf('-');
        if (idx > 0 && idx < text.Length - 1)
        {
            string name = text.Substring(0, idx).Trim();
            int delta = ParseValue(text.Substring(idx + 1).Trim(), full);
            vars.Set(name, vars.Get(name) - delta);
            return;
        }

        throw new FormatException($"赋值表达式无法解析（应为 name=value / name+n / name-n）：\"{text}\"（整串：\"{full}\"）");
    }

    /// <summary>"=" 位置是否属于比较运算符（==/!=/&gt;=/&lt;=）的一部分——赋值字段里不该出现，防御串写错。</summary>
    private static bool IsComparePosition(string text, int eqIndex)
    {
        char prev = eqIndex > 0 ? text[eqIndex - 1] : '\0';
        char next = eqIndex < text.Length - 1 ? text[eqIndex + 1] : '\0';
        return prev == '=' || prev == '!' || prev == '>' || prev == '<' || next == '=';
    }

    private static int ParseValue(string text, string context)
    {
        if (text == "true")
        {
            return 1;
        }

        if (text == "false")
        {
            return 0;
        }

        if (int.TryParse(text, out var value))
        {
            return value;
        }

        throw new FormatException($"值无法解析（仅支持 true/false/整数）：\"{text}\"（上下文：\"{context}\"）");
    }
}
