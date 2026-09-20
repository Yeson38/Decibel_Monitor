using System;

namespace Decibel_Monitor.Alerting;

/// <summary>
/// 修饰键状态（与 KeyboardCapture 的 <c>KeyModifiers</c> 语义一致，此处独立定义以保持纯逻辑可单测）。
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    /// <summary>无修饰键。</summary>
    None = 0,

    /// <summary>Ctrl 键（左或右）。</summary>
    Ctrl = 1,

    /// <summary>Alt 键（左或右）。</summary>
    Alt = 2,

    /// <summary>Shift 键（左或右）。</summary>
    Shift = 4,

    /// <summary>系统/徽标键（Windows 上为 Win 键）。</summary>
    Meta = 8,

    /// <summary>全部修饰键的掩码。</summary>
    All = Ctrl | Alt | Shift | Meta,
}

/// <summary>
/// 一个热键定义（键名 + 修饰键组合）。
/// </summary>
/// <param name="Key">键名，与 KeyboardCapture 的 <c>KeyboardKey.Name</c> 语义一致（如 <c>"A"</c>、<c>"F5"</c>、<c>"Space"</c>）。</param>
/// <param name="Modifiers">要求的修饰键组合（须完全一致才算命中）。</param>
public readonly record struct HotkeyDefinition(string Key, HotkeyModifiers Modifiers)
{
    /// <summary>未配置的热键（不会命中任何按键）。</summary>
    public static HotkeyDefinition None => new(string.Empty, HotkeyModifiers.None);

    /// <summary>是否已配置有效的键名。</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Key);

    /// <summary>
    /// 判定一次按键事件是否命中本热键（键名忽略大小写，修饰键须完全一致）。
    /// </summary>
    public bool Matches(string? keyName, HotkeyModifiers modifiers)
    {
        if (!IsValid || string.IsNullOrWhiteSpace(keyName)) return false;

        return string.Equals(Key, keyName, StringComparison.OrdinalIgnoreCase)
               && (modifiers & HotkeyModifiers.All) == (Modifiers & HotkeyModifiers.All);
    }
}
