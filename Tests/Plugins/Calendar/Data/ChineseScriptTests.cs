using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.Calendar.Data;

namespace Lertaro.Plugins.Calendar.Tests.Data;

[TestClass]
[DoNotParallelize]
public sealed class ChineseScriptTests
{
    private Func<string> _originalCultureFunc = null!;

    [TestInitialize]
    public void SaveSeam() => _originalCultureFunc = TranslationService.CurrentCultureFunc;

    [TestCleanup]
    public void RestoreSeam()
    {
        TranslationService.CurrentCultureFunc = _originalCultureFunc;
        TranslationService.NotifyCultureChanged(_originalCultureFunc());
    }

    private static void UseCulture(string name) => TranslationService.CurrentCultureFunc = () => name;

    [TestMethod]
    public void SimplifiedRegion_LeavesTheTextAlone()
    {
        UseCulture("zh-CN");

        Assert.AreEqual("谷雨", ChineseScript.ForRegion("谷雨"));
        Assert.AreEqual("丙午马年", ChineseScript.ForRegion("丙午马年"));
    }

    [TestMethod]
    public void TraditionalRegions_ConvertTheLibraryText()
    {
        UseCulture("zh-TW");
        Assert.AreEqual("穀雨", ChineseScript.ForRegion("谷雨"));
        Assert.AreEqual("丙午馬年", ChineseScript.ForRegion("丙午马年"));
        // Punctuation is not part of the conversion: the numeric lunar mode's slash and the library's
        // own parentheses have to survive untouched.
        Assert.AreEqual("8/19", ChineseScript.ForRegion("8/19"));

        UseCulture("zh-HK");
        Assert.AreEqual("穀雨", ChineseScript.ForRegion("谷雨"));
    }

    [TestMethod]
    public void Regions_GetTheirOwnVariant()
    {
        // The two Traditional regions disagree on a handful of characters, and the converter is asked for
        // each region's own preset rather than one shared answer: 台 becomes 臺 in Taiwan and stays 台 in
        // Hong Kong, and 床 the other way round.
        UseCulture("zh-TW");
        Assert.AreEqual("三臺", ChineseScript.ForRegion("三台"));
        Assert.AreEqual("申不安床鬼祟入房", ChineseScript.ForRegion("申不安床鬼祟入房"));

        UseCulture("zh-HK");
        Assert.AreEqual("三台", ChineseScript.ForRegion("三台"));
        Assert.AreEqual("申不安牀鬼祟入房", ChineseScript.ForRegion("申不安床鬼祟入房"));
    }

    [TestMethod]
    public void Conversion_IsPhraseLevelNotCharacterLevel()
    {
        // The reason this is a library and not a character map: the same Simplified character has to come
        // out differently depending on the word it stands in.
        UseCulture("zh-TW");

        Assert.AreEqual("干支", ChineseScript.ForRegion("干支"), "the 干 of 干支 is not 乾");
        Assert.AreEqual("凶神", ChineseScript.ForRegion("凶神"), "the almanac's 凶神 is not 兇神");
        Assert.AreEqual("築堤", ChineseScript.ForRegion("筑堤"));
        Assert.AreEqual("修飾垣牆", ChineseScript.ForRegion("修饰垣墙"));
        Assert.AreEqual("遊禍", ChineseScript.ForRegion("游祸"));
        Assert.AreEqual("己不破券二比並亡", ChineseScript.ForRegion("己不破券二比并亡"));
        Assert.AreEqual("衝(壬寅)虎 煞南", ChineseScript.ForRegion("冲(壬寅)虎 煞南"));
    }

    [TestMethod]
    public void EmptyText_IsReturnedAsIs()
    {
        UseCulture("zh-TW");

        Assert.AreEqual(string.Empty, ChineseScript.ForRegion(string.Empty));
        Assert.IsNull(ChineseScript.ForRegion(null!));
    }

    [TestMethod]
    public void DayInfo_ConvertsTheTextAndKeepsEverythingElse()
    {
        UseCulture("zh-TW");
        var date = new DateTime(2026, 4, 20);

        var converted = ChineseScript.ForRegion(new DayInfo(
            date, "八月", "谷雨", NoteKind.SolarTerm, DutyKind.Off, "国庆节",
            InMonth: true, IsWeekend: false, IsToday: true));

        Assert.AreEqual("穀雨", converted.NoteText);
        Assert.AreEqual("國慶節", converted.DutyName);
        Assert.AreEqual(date, converted.Date);
        Assert.AreEqual(NoteKind.SolarTerm, converted.Note);
        Assert.AreEqual(DutyKind.Off, converted.Duty);
        Assert.IsTrue(converted.InMonth);
        Assert.IsTrue(converted.IsToday);
    }

    [TestMethod]
    public void AlmanacInfo_ConvertsEveryFieldItCarries()
    {
        UseCulture("zh-TW");

        var converted = ChineseScript.ForRegion(new AlmanacInfo(
            new DateTime(2026, 10, 1),
            "八月廿一", "丙午马年", "丁酉月", "戊申日", "大驿土", "冲(壬寅)虎 煞南",
            "戊不受田田主不祥", "申不安床鬼祟入房", "白虎 黑道 凶", "东南", "东北", "正北",
            "冠笄 沐浴 出行 修造 动土", "嫁娶 开市 祭祀", "天赦 王日 天马", "游祸 血支 五离"));

        Assert.AreEqual("丙午馬年", converted.YearPillar);
        Assert.AreEqual("大驛土", converted.FiveElements);
        Assert.AreEqual("衝(壬寅)虎 煞南", converted.ChongSha);
        Assert.AreEqual("冠笄 沐浴 出行 修造 動土", converted.Yi);
        Assert.AreEqual("嫁娶 開市 祭祀", converted.Ji);
        Assert.AreEqual("天赦 王日 天馬", converted.LuckySpirits);
        Assert.AreEqual("遊禍 血支 五離", converted.EvilSpirits);
        Assert.AreEqual("戊不受田田主不祥", converted.PengZuGan, "this line has nothing to convert");
    }
}
