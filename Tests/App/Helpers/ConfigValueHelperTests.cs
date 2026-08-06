using System.Text.Json;
using Lertaro.App.Helpers;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.Helpers;

[TestClass]
public sealed class ConfigValueHelperTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [TestMethod]
    public void UnpackValue_NonJsonElement_ReturnsAsIs() =>
        Assert.AreEqual("plain", ConfigValueHelper.UnpackValue("plain"));

    [TestMethod]
    public void UnpackValue_JsonString_ReturnsString() =>
        Assert.AreEqual("hello", ConfigValueHelper.UnpackValue(Parse("\"hello\"")));

    [TestMethod]
    public void UnpackValue_JsonIntNumber_ReturnsInt32() =>
        Assert.AreEqual(42, ConfigValueHelper.UnpackValue(Parse("42")));

    [TestMethod]
    public void UnpackValue_JsonLargeNumber_ReturnsInt64()
    {
        var result = ConfigValueHelper.UnpackValue(Parse("5000000000"));

        Assert.AreEqual(5_000_000_000L, result);
    }

    [TestMethod]
    public void UnpackValue_JsonDecimalNumber_ReturnsDouble() =>
        Assert.AreEqual(3.5, ConfigValueHelper.UnpackValue(Parse("3.5")));

    [TestMethod]
    public void UnpackValue_JsonTrue_ReturnsTrue() =>
        Assert.IsTrue((bool)ConfigValueHelper.UnpackValue(Parse("true"))!);

    [TestMethod]
    public void UnpackValue_JsonFalse_ReturnsFalse() =>
        Assert.IsFalse((bool)ConfigValueHelper.UnpackValue(Parse("false"))!);

    [TestMethod]
    public void UnpackValue_JsonNull_ReturnsNull() =>
        Assert.IsNull(ConfigValueHelper.UnpackValue(Parse("null")));

    [TestMethod]
    public void UnpackValue_JsonArray_ReturnsUnpackedList()
    {
        var result = ConfigValueHelper.UnpackValue(Parse("[1, \"a\", true]")) as List<object>;

        Assert.IsNotNull(result);
        Assert.HasCount(3, result);
        Assert.AreEqual(1, result[0]);
        Assert.AreEqual("a", result[1]);
        Assert.IsTrue((bool)result[2]);
    }

    [TestMethod]
    public void UnpackValue_JsonObject_ReturnsCaseInsensitiveUnpackedDictionary()
    {
        var result = ConfigValueHelper.UnpackValue(Parse("{\"Key\": 1}")) as Dictionary<string, object>;

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result["key"]);
    }

    [TestMethod]
    public void UnpackValue_NestedArrayOfObjects_UnpacksRecursively()
    {
        var result = ConfigValueHelper.UnpackValue(Parse("[{\"a\": 1}]")) as List<object>;

        Assert.IsNotNull(result);
        var inner = result[0] as Dictionary<string, object>;
        Assert.IsNotNull(inner);
        Assert.AreEqual(1, inner["a"]);
    }

    private sealed class SamplePoco
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
    }

    // A plugin writing a strongly-typed object (e.g. List<SomePocoClass>) via PluginSettingsService.
    // SetSetting must end up in exactly this shape -- PluginManager.SetPluginSetting normalizes through
    // JsonSerializer.SerializeToElement before storing, so this is what a written-then-immediately-read
    // array field's raw value actually looks like (matching what a fresh disk reload of UserSettings
    // would also produce, since System.Text.Json deserializes an `object`-typed property as JsonElement
    // either way). Without that normalization, a raw POCO list doesn't match the `is JsonElement` check
    // at all and UnpackValue returns it unchanged (see UnpackValue_NonJsonElement_ReturnsAsIs above) --
    // PluginConfigArrayFieldSupport's own Dictionary<string, object> cast then fails per item, and the
    // Settings UI renders the row with every field blank.
    [TestMethod]
    public void UnpackValue_JsonElementFromSerializedPocoList_UnpacksToDictionariesKeyedByPropertyName()
    {
        var pocoList = new List<SamplePoco> { new() { Name = "Downloads", Path = @"C:\Downloads" } };
        var element = JsonSerializer.SerializeToElement(pocoList);

        var result = ConfigValueHelper.UnpackValue(element) as List<object>;

        Assert.IsNotNull(result);
        var inner = result.Single() as Dictionary<string, object>;
        Assert.IsNotNull(inner);
        Assert.AreEqual("Downloads", inner["Name"]);
        Assert.AreEqual(@"C:\Downloads", inner["Path"]);
    }

    [TestMethod]
    public void ConvertValue_Null_ReturnsNull() =>
        Assert.IsNull(ConfigValueHelper.ConvertValue(null, ConfigFieldType.Integer));

    [TestMethod]
    public void ConvertValue_IntegerFieldWithNumericString_ReturnsParsedInt() =>
        Assert.AreEqual(42, ConfigValueHelper.ConvertValue("42", ConfigFieldType.Integer));

    [TestMethod]
    public void ConvertValue_IntegerFieldWithNonNumericString_ReturnsOriginalString() =>
        Assert.AreEqual("abc", ConfigValueHelper.ConvertValue("abc", ConfigFieldType.Integer));

    [TestMethod]
    public void ConvertValue_IntegerFieldWithBoxedInt_ReturnsSameValue() =>
        Assert.AreEqual(7, ConfigValueHelper.ConvertValue(7, ConfigFieldType.Integer));

    [TestMethod]
    public void ConvertValue_IntegerFieldWithBoxedDouble_ConvertsToInt32UsingBankersRounding() =>
        // Convert.ToInt32(double) rounds to nearest (MidpointRounding.ToEven-based), not truncates.
        Assert.AreEqual(8, ConfigValueHelper.ConvertValue(7.9, ConfigFieldType.Integer));

    [TestMethod]
    public void ConvertValue_NonIntegerField_ReturnsValueUnchanged() =>
        Assert.AreEqual("hello", ConfigValueHelper.ConvertValue("hello", ConfigFieldType.Text));

    [TestMethod]
    public void ConvertValue_IntegerFieldWithUnconvertibleObject_ReturnsOriginalValue()
    {
        var obj = new object();

        Assert.AreSame(obj, ConfigValueHelper.ConvertValue(obj, ConfigFieldType.Integer));
    }
}
