using System.Text.Json;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Helpers;

public static class ConfigValueHelper
{
    public static object? UnpackValue(object? val)
    {
        if (val is JsonElement elem)
        {
            switch (elem.ValueKind)
            {
                case JsonValueKind.String:
                    return elem.GetString();
                case JsonValueKind.Number:
                    if (elem.TryGetInt32(out var i)) return i;
                    if (elem.TryGetInt64(out var l)) return l;
                    return elem.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Object:
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(elem.GetRawText());
                    if (dict != null)
                    {
                        var unpackedDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kvp in dict)
                        {
                            unpackedDict[kvp.Key] = UnpackValue(kvp.Value)!;
                        }
                        return unpackedDict;
                    }
                    return dict;
                case JsonValueKind.Array:
                    var list = JsonSerializer.Deserialize<List<object>>(elem.GetRawText());
                    if (list != null)
                    {
                        return list.Select(UnpackValue).ToList()!;
                    }
                    return list;
            }
        }
        return val;
    }

    public static object? ConvertValue(object? val, ConfigFieldType fieldType)
    {
        if (val == null) return null;
        if (fieldType == ConfigFieldType.Integer)
        {
            if (val is string strVal)
            {
                if (int.TryParse(strVal, out var parsedInt))
                    return parsedInt;
            }
            else
            {
                try
                {
                    return Convert.ToInt32(val);
                }
                catch { }
            }
        }
        return val;
    }
}
