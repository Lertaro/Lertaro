namespace Lertaro.App.Services.ShellMenu.ActionFlyout;

/// <summary>
/// Finds the action-menu item a typed letter stands for, so a menu can be driven by mnemonics.
/// </summary>
/// <remarks>
/// Split out of the key handling (and kept free of any window type) so the rule is pinned by a test:
/// an item is matched by the single-character <see cref="ActionMenuItem.ShortcutHint"/> a provider gave
/// it, case-insensitively, and only when it is something a click could actually run -- a section header,
/// a separator or a disabled row must never be the one a letter picks.
/// </remarks>
internal static class ActionsMenuShortcutMatcher
{
    /// <summary>
    /// The first runnable item whose shortcut hint is <paramref name="letter"/>, or null when no item
    /// claims it. Comparison is case-insensitive in both directions: providers write the hint however
    /// their own docs read ("P" or "p"), and the letter arrives as the key's uppercase form.
    /// </summary>
    public static ActionMenuItem? Find(IEnumerable<ActionMenuItem> items, char letter)
    {
        var wanted = char.ToUpperInvariant(letter);

        foreach (var item in items)
        {
            if (item.IsSeparator || item.IsSectionHeader || item.IsDisabled) continue;

            // One character only: a hint like "Ctrl+O" is a displayed hotkey, not a mnemonic, and must
            // not be matched by the 'O' of "Ctrl+O".
            var hint = item.ShortcutHint;
            if (hint.Length == 1 && char.ToUpperInvariant(hint[0]) == wanted) return item;
        }

        return null;
    }
}
