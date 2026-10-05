using System.IO;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.Calendar.Data;
using Lertaro.Plugins.Calendar.Reminders;

namespace Lertaro.Plugins.Calendar;

/// <summary>
/// Entry point for the Calendar plugin: its settings page, and the process-wide home of the reminder
/// engine.
/// </summary>
/// <remarks>
/// <para>
/// The engine is static because the loader instantiates this assembly once <em>per</em> interface its
/// types implement, so the object behind <see cref="IPlugin"/> and the one behind
/// <see cref="IInstantResultProvider"/> are different instances that share no fields. Anything that has
/// to outlive a single component has to live on the type. <see cref="IPlugin"/> and
/// <see cref="IConfigurable"/> do stay together, because the settings page resolves the configurable
/// through the already-registered instance and would otherwise be handed a throwaway.
/// </para>
/// <para>
/// There is no plugin start hook, so this static constructor is it. Two startup orders are load-bearing
/// here: the schema default map is not built until after plugins load, so every read passes its own
/// default rather than relying on the field's; and the translation lookup is wired after the plugin
/// manager is first realised, so nothing below may ask for a translated string.
/// </para>
/// </remarks>
public sealed class CalendarPlugin : IPlugin, IConfigurable
{
    internal const string PluginId = "Lertaro.Plugins.Calendar";
    internal const string DefaultTriggerKeyword = "date";

    /// <summary>The reminder's own display time, which this plugin's settings page owns. The host clips it
    /// to whatever range the notification position allows.</summary>
    internal const string ReminderDurationKey = "ReminderDurationSeconds";
    internal const int DefaultReminderDurationSeconds = 8;

    /// <summary>
    /// Whether the quick window's clock line carries this plugin's 农历 reading. Owned here rather than in the
    /// host's own Layout settings on purpose: the text is this plugin's data, so switching it off belongs in
    /// the same page as the rest of the calendar's settings, and a plugin the user has disabled is never asked
    /// for it at all -- which is the difference the host-side flag could not express.
    /// </summary>
    internal const string LunarInClockKey = "LunarInClock";

    private static readonly string PluginDllName = Path.GetFileName(typeof(CalendarPlugin).Assembly.Location);
    private static readonly object RuntimeLock = new();

    internal static ReminderStore? Store { get; private set; }
    internal static ReminderEngine? Engine { get; private set; }

    static CalendarPlugin()
    {
        try
        {
            PluginSettingsService.ComponentEnablementChanged += UpdateRuntimeState;
            PluginSettingsService.SettingChanged += (id, _) =>
            {
                if (string.Equals(id, PluginId, StringComparison.OrdinalIgnoreCase))
                    UpdateRuntimeState();
            };
            UpdateRuntimeState();
        }
        catch (Exception ex)
        {
            // An escaping throw here surfaces as TypeInitializationException and takes every component in
            // this assembly down with it, including the provider that would have said why.
            Logger.Log($"[Calendar] startup failed, the plugin is inactive until restart: {ex}", LogLevel.Error);
        }
    }

    public string Name => TranslationService.Get("Calendar_PluginName");
    public string Description => TranslationService.Get("Calendar_PluginDesc");

    public PluginConfigSchema GetConfigSchema() => PruneForLanguage(new()
    {
        Fields =
        [
            new PluginConfigField
            {
                Key = "TriggerKeyword",
                IsTriggerWord = true,
                LabelKey = "Calendar_Config_TriggerLabel",
                DescriptionKey = "Calendar_Config_TriggerDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = DefaultTriggerKeyword,
                RequireNonEmpty = true
            },
            new PluginConfigField
            {
                Key = "FirstDayOfWeek",
                LabelKey = "Calendar_Config_FirstDayLabel",
                DescriptionKey = "Calendar_Config_FirstDayDesc",
                FieldType = ConfigFieldType.Choice,
                DefaultValue = ChineseCalendar.FirstDayCulture,
                ChoiceOptions =
                [
                    Choice(ChineseCalendar.FirstDayMonday, "Calendar_Config_FirstDayMonday"),
                    Choice(ChineseCalendar.FirstDaySunday, "Calendar_Config_FirstDaySunday"),
                    Choice(ChineseCalendar.FirstDayCulture, "Calendar_Config_FirstDayCulture")
                ]
            },
            new PluginConfigField
            {
                Key = "LunarDisplay",
                LabelKey = "Calendar_Config_LunarLabel",
                DescriptionKey = "Calendar_Config_LunarDesc",
                FieldType = ConfigFieldType.Choice,
                DefaultValue = ChineseCalendar.LunarChinese,
                ChoiceOptions =
                [
                    Choice(ChineseCalendar.LunarChinese, "Calendar_Config_LunarChinese"),
                    Choice(ChineseCalendar.LunarNumeric, "Calendar_Config_LunarNumeric"),
                    Choice(ChineseCalendar.LunarHidden, "Calendar_Config_LunarHidden")
                ]
            },
            new PluginConfigField
            {
                Key = "ShowStatutoryHolidays",
                LabelKey = "Calendar_Config_HolidaysLabel",
                DescriptionKey = "Calendar_Config_HolidaysDesc",
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = true
            },
            new PluginConfigField
            {
                Key = LunarInClockKey,
                LabelKey = "Calendar_Config_ClockLabel",
                DescriptionKey = "Calendar_Config_ClockDesc",
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = true
            },
            new PluginConfigField
            {
                Key = ReminderDurationKey,
                LabelKey = "Calendar_Config_DurationLabel",
                DescriptionKey = "Calendar_Config_DurationDesc",
                FieldType = ConfigFieldType.Integer,
                DefaultValue = DefaultReminderDurationSeconds
            },
            new PluginConfigField
            {
                Key = "SendTestReminder",
                LabelKey = "Calendar_Config_TestLabel",
                DescriptionKey = "Calendar_Config_TestDesc",
                FieldType = ConfigFieldType.Button,
                DefaultValue = string.Empty,
                // Off the settings UI thread: this writes the reminder file and runs a catch-up pass.
                OnClick = () => Task.Run(SendTestReminder)
            }
        ],
        OnSave = UpdateRuntimeState,
        OnRollback = UpdateRuntimeState
    });

    /// <summary>
    /// Drops the settings that configure a layer the current interface language never shows. The app reads a
    /// plugin's schema once, at load, so switching language takes effect on the next start rather than in the
    /// settings page that is already open.
    /// </summary>
    private static PluginConfigSchema PruneForLanguage(PluginConfigSchema schema)
    {
        if (!CalendarText.ShowsChineseCalendar)
            schema.Fields.RemoveAll(f => f.Key is "LunarDisplay" or "ShowStatutoryHolidays" or LunarInClockKey);
        return schema;
    }

    private static PluginConfigChoice Choice(string value, string labelKey) =>
        new() { Value = value, LabelKey = labelKey };

    internal static string TriggerKeyword() =>
        TriggerWord.Normalize(PluginSettingsService.GetSetting(PluginId, "TriggerKeyword", DefaultTriggerKeyword));

    internal static string FirstDayOfWeekSetting() =>
        PluginSettingsService.GetSetting(PluginId, "FirstDayOfWeek", ChineseCalendar.FirstDayCulture);

    internal static string LunarDisplaySetting() =>
        PluginSettingsService.GetSetting(PluginId, "LunarDisplay", ChineseCalendar.LunarChinese);

    internal static bool ShowStatutoryHolidays() =>
        PluginSettingsService.GetSetting(PluginId, "ShowStatutoryHolidays", true);

    /// <summary>Whether the clock line takes this plugin's 农历 reading. On unless turned off here.</summary>
    internal static bool ShowLunarInClock() =>
        PluginSettingsService.GetSetting(PluginId, LunarInClockKey, true);

    internal static int ReminderDurationSeconds() =>
        PluginSettingsService.GetSetting(PluginId, ReminderDurationKey, DefaultReminderDurationSeconds);

    private static void UpdateRuntimeState()
    {
        lock (RuntimeLock)
        {
            // Without this gate a disabled plugin keeps announcing reminders: the static constructor has
            // already run, and nothing else in the process ever revisits it.
            if (!PluginSettingsService.IsComponentEnabled(PluginDllName, "InstantProvider", nameof(CalendarInstantProvider)))
            {
                Engine?.Stop();
                Engine = null;
                return;
            }

            var store = Store ??= new ReminderStore();
            var engine = Engine ??= new ReminderEngine(store, ReminderNotifier.Show);
            engine.Start();
        }
    }

    private static void SendTestReminder()
    {
        lock (RuntimeLock)
        {
            UpdateRuntimeState();
            if (Store == null || Engine == null) return;

            var now = DateTime.Now;
            // Due immediately rather than a minute out, so the whole path, file write, tick, host
            // notification and click-back, is checkable in seconds instead of on a delay.
            Store.Add(new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Unspecified),
                TranslationService.Get("Calendar_TestReminderText"));
            Engine.ReconcileSoon();
        }
    }
}
