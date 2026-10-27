using System.Text.Json.Serialization;

namespace Lertaro.Plugins.CoreExtensions.Models;

/// <summary>
/// One entry category: a keyword the user types, and -- when <see cref="ShowAsRow"/> is set -- a row the
/// host shows. This is deliberately ONE list for both: the old split, where the typed keyword lived here and
/// the click target lived in a separate sidebar list of the same shape, let the same category be defined twice
/// with two different rules and made a row's keyword double as its untranslated display name.
/// </summary>
public class CustomFilterItem
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// What the user types after the category prefix, and what another rule's <c>@keyword</c> reference
    /// resolves against. Also the display name when <see cref="Label"/> is empty.
    /// </summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>
    /// The name a row shows, so a row can read 文档 in one language and the keyword stay <c>doc</c> in every
    /// one. Empty means the keyword itself, which is what the sidebar list used to do unconditionally.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Vector icon path data for the row. Empty uses the host's default mark.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Whether this category also appears as a row in the full window's sidebar and in the quick window's
    /// filter bar. Default false: an entry stays a typed keyword until it is asked for as a row, which is
    /// what keeps the six built-in keywords from duplicating the built-in rows that are already on.
    /// </summary>
    public bool ShowAsRow { get; set; }

    public string Rule { get; set; } = string.Empty;

    /// <summary>What a row should read as: the label when there is one, the keyword otherwise. Ignored on
    /// serialize, so a stored entry keeps exactly the fields the settings page shows it.</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Keyword.Trim() : Label.Trim();
}
