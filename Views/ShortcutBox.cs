using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TimeInset.Core;
using TimeInset.Core.Localization;

namespace TimeInset.Views;
/// <summary>
/// ショートカットの入力欄（PlainClip・PresenterToolsと同じ操作）。フォーカスしてキーを押すと組み合わせが入る。
/// Backspace / Delete を単独で押すと未割当にする。修飾なしの Tab・Esc はフォーカス移動・画面のキャンセルに渡す。
/// フォーカス中は <see cref="CaptureStarted"/> / <see cref="CaptureEnded"/> で既存のショートカットを一時解除してもらう。
/// </summary>
public sealed class ShortcutBox : TextBox
{
    private Hotkey? shortcut;
    public ShortcutBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        InputMethod.SetIsInputMethodEnabled(this, false);
        VerticalContentAlignment = VerticalAlignment.Center;
        UpdateText();
    }
    public event EventHandler? CaptureStarted;
    public event EventHandler? CaptureEnded;
    public event EventHandler? ShortcutChanged;
    /// <summary>修飾キーなし・Win・F12など、登録できない組み合わせが押された。</summary>
    public event EventHandler? Rejected;
    public Hotkey? Shortcut
    {
        get => shortcut;
        set
        {
            if (shortcut == value) return;
            shortcut = value;
            UpdateText();
            ShortcutChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); CaptureStarted?.Invoke(this, EventArgs.Empty); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); CaptureEnded?.Invoke(this, EventArgs.Empty); }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
        var mods = Keyboard.Modifiers;
        if (key == Key.Tab && mods is ModifierKeys.None or ModifierKeys.Shift) return;
        if (key == Key.Escape && mods == ModifierKeys.None) return;
        e.Handled = true;
        if (mods == ModifierKeys.None && key is Key.Back or Key.Delete) { Shortcut = null; return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        uint m = (mods.HasFlag(ModifierKeys.Alt) ? 1u : 0) | (mods.HasFlag(ModifierKeys.Control) ? 2u : 0) |
                 (mods.HasFlag(ModifierKeys.Shift) ? 4u : 0) | (mods.HasFlag(ModifierKeys.Windows) ? 8u : 0);
        var combo = new Hotkey(m, (uint)KeyInterop.VirtualKeyFromKey(key));
        if (combo.IsValid) Shortcut = combo;
        else Rejected?.Invoke(this, EventArgs.Empty);
    }
    private void UpdateText() => Text = shortcut?.ToString() ?? Strings.Unassigned;
}
