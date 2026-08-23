using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Controls;
using System.Windows.Media;

namespace Flow.Launcher.Plugin;

/// <summary>
/// Describes a result of a Query executed by a plugin.
/// </summary>
public class Result
{
    public const int MaxScore = int.MaxValue;

    private string _pluginDirectory = string.Empty;
    private string _icoPath = string.Empty;
    private string _copyText = string.Empty;
    private string _badgeIcoPath = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string SubTitle { get; set; } = string.Empty;
    public string ActionKeywordAssigned { get; set; } = string.Empty;

    public string CopyText
    {
        get => string.IsNullOrEmpty(_copyText) ? SubTitle : _copyText;
        set => _copyText = value;
    }

    public string AutoCompleteText { get; set; } = string.Empty;

    public string IcoPath
    {
        get => _icoPath;
        set
        {
            if (!string.IsNullOrEmpty(value)
                && !string.IsNullOrEmpty(PluginDirectory)
                && !Path.IsPathRooted(value)
                && !value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                _icoPath = Path.Combine(PluginDirectory, value);
            }
            else
            {
                _icoPath = value;
            }
        }
    }

    public string IcoPathAbsolute => _icoPath;

    public string BadgeIcoPath
    {
        get => _badgeIcoPath;
        set
        {
            if (!string.IsNullOrEmpty(value)
                && !string.IsNullOrEmpty(PluginDirectory)
                && !Path.IsPathRooted(value)
                && !value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                _badgeIcoPath = Path.Combine(PluginDirectory, value);
            }
            else
            {
                _badgeIcoPath = value;
            }
        }
    }

    public bool RoundedIcon { get; set; }

    public delegate ImageSource? IconDelegate();

    [JsonIgnore]
    public IconDelegate? Icon;

    [JsonIgnore]
    public IconDelegate? BadgeIcon;

    public GlyphInfo Glyph { get; set; }

    [JsonIgnore]
    public Func<ActionContext, bool>? Action { get; set; }

    [JsonIgnore]
    public Func<ActionContext, ValueTask<bool>>? AsyncAction { get; set; }

    public int Score { get; set; }
    public IList<int>? TitleHighlightData { get; set; }
    public IList<int>? SubTitleHighlightData { get; set; }

    [JsonIgnore]
    public object? ContextData { get; set; }

    public string PluginID { get; set; } = string.Empty;
    public string? TitleToolTip { get; set; }
    public string? SubTitleToolTip { get; set; }

    [JsonIgnore]
    public Lazy<UserControl>? PreviewPanel { get; set; }

    public int? ProgressBar { get; set; }
    public string ProgressBarColor { get; set; } = "#26a0da";

    public PreviewInfo Preview { get; set; } = PreviewInfo.Default;
    public bool AddSelectedCount { get; set; } = true;
    public string? RecordKey { get; set; }
    public bool ShowBadge { get; set; }
    public string? QuerySuggestionText { get; set; }

    public string PluginDirectory
    {
        get => _pluginDirectory;
        set
        {
            _pluginDirectory = value;
            if (!string.IsNullOrEmpty(IcoPath))
                IcoPath = IcoPath;
            if (!string.IsNullOrEmpty(BadgeIcoPath))
                BadgeIcoPath = BadgeIcoPath;
        }
    }

    internal Query? OriginQuery { get; set; }

    public ValueTask<bool> ExecuteAsync(ActionContext context)
    {
        return AsyncAction?.Invoke(context) ?? ValueTask.FromResult(Action?.Invoke(context) ?? false);
    }

    public Result Clone()
    {
        return new Result
        {
            Title = Title,
            SubTitle = SubTitle,
            ActionKeywordAssigned = ActionKeywordAssigned,
            CopyText = CopyText,
            AutoCompleteText = AutoCompleteText,
            IcoPath = IcoPath,
            BadgeIcoPath = BadgeIcoPath,
            RoundedIcon = RoundedIcon,
            Icon = Icon,
            BadgeIcon = BadgeIcon,
            Glyph = Glyph,
            Action = Action,
            AsyncAction = AsyncAction,
            Score = Score,
            TitleHighlightData = TitleHighlightData,
            OriginQuery = OriginQuery,
            PluginDirectory = PluginDirectory,
            ContextData = ContextData,
            PluginID = PluginID,
            TitleToolTip = TitleToolTip,
            SubTitleToolTip = SubTitleToolTip,
            PreviewPanel = PreviewPanel,
            ProgressBar = ProgressBar,
            ProgressBarColor = ProgressBarColor,
            Preview = Preview,
            AddSelectedCount = AddSelectedCount,
            RecordKey = RecordKey,
            ShowBadge = ShowBadge,
            QuerySuggestionText = QuerySuggestionText
        };
    }

    public override string ToString() => Title + SubTitle + Score;

    public record PreviewInfo
    {
        public string? PreviewImagePath { get; set; }
        public bool IsMedia { get; set; }
        public string? Description { get; set; }

        [JsonIgnore]
        public IconDelegate? PreviewDelegate;

        public string? FilePath { get; set; }

        public static PreviewInfo Default { get; } = new()
        {
            PreviewImagePath = null,
            Description = null,
            IsMedia = false,
            PreviewDelegate = null,
            FilePath = null
        };
    }
}
