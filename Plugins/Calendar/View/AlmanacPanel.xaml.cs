using System.Windows.Controls;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.Calendar.Data;

namespace Lertaro.Plugins.Calendar.View;

/// <summary>
/// Displays the selected day's almanac: 干支, 五行, 冲煞, 彭祖百忌, 值神吉凶, auspicious directions, 宜/忌, and 吉神/凶神.
/// </summary>
internal partial class AlmanacPanel : UserControl
{
    public AlmanacPanel()
    {
        InitializeComponent();
        ApplyLabels();
    }

    /// <summary>The label cells, refreshed on construction and on a language switch.</summary>
    public void ApplyLabels()
    {
        FiveElementsLabelText.Text = TranslationService.Get("Calendar_AlmanacFiveElements");
        ChongShaLabelText.Text = TranslationService.Get("Calendar_AlmanacChongSha");
        PengZuLabelText.Text = TranslationService.Get("Calendar_AlmanacPengZu");
        LuckLabelText.Text = TranslationService.Get("Calendar_AlmanacLuck");
        JoyGodLabelText.Text = TranslationService.Get("Calendar_AlmanacJoyGod");
        FortuneGodLabelText.Text = TranslationService.Get("Calendar_AlmanacFortuneGod");
        WealthGodLabelText.Text = TranslationService.Get("Calendar_AlmanacWealthGod");
        YiBadgeText.Text = TranslationService.Get("Calendar_AlmanacYi");
        JiBadgeText.Text = TranslationService.Get("Calendar_AlmanacJi");
        LuckySpiritsLabelText.Text = TranslationService.Get("Calendar_AlmanacLuckySpirits");
        EvilSpiritsLabelText.Text = TranslationService.Get("Calendar_AlmanacEvilSpirits");
    }

    /// <summary>
    /// Fills the panel for one day. The weekday is the only value that follows the interface language,
    /// because that one is a locale datum rather than almanac data.
    /// </summary>
    public void Apply(AlmanacInfo info)
    {
        AlmanacLunarText.Text = TranslationService.Get("Calendar_AlmanacPrefix") + info.LunarDate;
        AlmanacYearText.Text = info.YearPillar;
        AlmanacMonthText.Text = info.MonthPillar;
        AlmanacDayText.Text = info.DayPillar;
        AlmanacWeekdayText.Text = CalendarText.DayName(info.Date);

        FiveElementsText.Text = info.FiveElements;
        ChongShaText.Text = info.ChongSha;
        PengZuGanText.Text = info.PengZuGan;
        PengZuZhiText.Text = info.PengZuZhi;
        LuckText.Text = info.Luck;

        JoyGodText.Text = info.JoyGodDirection;
        FortuneGodText.Text = info.FortuneGodDirection;
        WealthGodText.Text = info.WealthGodDirection;

        YiText.Text = info.Yi;
        JiText.Text = info.Ji;
        LuckySpiritsText.Text = info.LuckySpirits;
        EvilSpiritsText.Text = info.EvilSpirits;
    }
}
