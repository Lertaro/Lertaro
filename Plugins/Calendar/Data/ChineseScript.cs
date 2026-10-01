using OpenccNetLib;

namespace Lertaro.Plugins.Calendar.Data;

/// <summary>
/// Puts the library's Simplified Chinese into the script the interface region writes in, because every
/// string the almanac and the lunar sublabels are made of comes out of lunar-csharp's tables in Simplified
/// and the Chinese-calendar layer is shown in zh-HK and zh-TW as well as zh-CN.
/// </summary>
/// <remarks>
/// The converter is OpenCC -- the dictionary-driven one the rest of the Chinese-speaking world uses --
/// because this conversion is phrase-level by nature: 谷雨 has to come out 穀雨 while 干支 must stay 干支,
/// and only a dictionary knows which is which. The regional configs are OpenCC's own too, so a Hong Kong
/// reader gets 啓鑽/三台/安牀 and a Taiwan reader 啟鑽/三臺/安床 without this file holding an opinion.
/// Punctuation is left alone on purpose: the library's own parentheses and the numeric lunar mode's slash
/// are not this converter's business.
/// </remarks>
internal static class ChineseScript
{
    private static readonly object Gate = new();
    private static Opencc? _tw;
    private static Opencc? _hk;

    /// <summary>
    /// <paramref name="text"/> in the interface region's script, or unchanged when the region writes
    /// Simplified: the library's own text is always better on screen than nothing.
    /// </summary>
    internal static string ForRegion(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (!CalendarText.UsesTraditionalScript) return text;

        var hongKong = CalendarText.Culture().Name == "zh-HK";
        var converter = Converter(hongKong);
        return hongKong ? converter.S2Hk(text, false) : converter.S2Tw(text, false);
    }

    /// <summary>
    /// The whole payload converted at once, so what reaches the screen is converted as a unit rather than
    /// field by field at each call site.
    /// </summary>
    internal static DayInfo ForRegion(DayInfo info) =>
        info with
        {
            LunarText = ForRegion(info.LunarText),
            NoteText = ForRegion(info.NoteText),
            DutyName = ForRegion(info.DutyName)
        };

    internal static AlmanacInfo ForRegion(AlmanacInfo info) =>
        info with
        {
            LunarDate = ForRegion(info.LunarDate),
            YearPillar = ForRegion(info.YearPillar),
            MonthPillar = ForRegion(info.MonthPillar),
            DayPillar = ForRegion(info.DayPillar),
            FiveElements = ForRegion(info.FiveElements),
            ChongSha = ForRegion(info.ChongSha),
            PengZuGan = ForRegion(info.PengZuGan),
            PengZuZhi = ForRegion(info.PengZuZhi),
            Luck = ForRegion(info.Luck),
            JoyGodDirection = ForRegion(info.JoyGodDirection),
            FortuneGodDirection = ForRegion(info.FortuneGodDirection),
            WealthGodDirection = ForRegion(info.WealthGodDirection),
            Yi = ForRegion(info.Yi),
            Ji = ForRegion(info.Ji),
            LuckySpirits = ForRegion(info.LuckySpirits),
            EvilSpirits = ForRegion(info.EvilSpirits)
        };

    // Built on first use rather than at startup: unpacking the embedded dictionaries costs ~30ms and a
    // process that never opens the calendar should never pay it. The API is synchronous, so unlike an
    // async converter factory there is no continuation to strand on a blocked UI thread. One instance per
    // region, cached, because a rebuild asks for 42 cells' worth of conversions.
    private static Opencc Converter(bool hongKong)
    {
        lock (Gate)
        {
            var built = hongKong ? _hk : _tw;
            if (built != null) return built;

            built = new Opencc(hongKong ? OpenccConfig.S2Hk : OpenccConfig.S2Tw);
            if (hongKong) _hk = built;
            else _tw = built;
            return built;
        }
    }
}
