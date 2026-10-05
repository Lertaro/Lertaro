namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Lets a plugin describe today's date for the host's own UI.
/// </summary>
/// <remarks>
/// The search box shows a date/time line in its placeholder slot when the box is empty (see the App's
/// "show clock" layout setting). That line is Chinese-calendar aware only because a plugin answers for it:
/// the 农历 date, the 节气 and the festivals come from tables the App has no business carrying, and a plugin
/// that already owns them can say what today is instead of the host growing a second copy.
///
/// This is a read-only extension point, not a search provider: the host calls it to fill one line of text on
/// a surface it owns. A plugin that does not implement it simply contributes nothing there.
/// </remarks>
public interface ICalendarTextProvider : IPluginComponent
{
    /// <summary>
    /// The extra text describing <paramref name="date"/>, to be shown after the host's own date and time --
    /// the 农历 reading, with a 节气 or festival in its place when one falls on that day. Empty when the
    /// plugin has nothing to add, which is the expected answer outside a Chinese interface language.
    /// </summary>
    /// <param name="date">
    /// The day to describe. Passed in rather than read from the clock by the implementation so the answer
    /// stays a function of its argument and can be tested.
    /// </param>
    string GetCalendarText(DateTime date);
}
