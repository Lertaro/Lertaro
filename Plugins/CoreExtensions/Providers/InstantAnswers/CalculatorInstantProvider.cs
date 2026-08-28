using System.Text.RegularExpressions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

public class CalculatorInstantProvider : IInstantResultProvider
{
    public string Name => TranslationService.Get("Calculator_Name");

    // Regex matching general math characters (digits, standard operators, parentheses, hex/bin prefixes, commas, and scientific function names)
    private static readonly Regex MathQueryRegex = new Regex(@"^[a-zA-Z0-9+\-*/%^(),.\sπ]+$", RegexOptions.Compiled);

    // Regex for base conversions e.g. "255 to hex", "0xFF to dec", "10 to bin"
    private static readonly Regex BaseConvRegex = new Regex(@"^\s*(0x[0-9a-fA-F]+|0b[01]+|\d+(\.\d+)?)\s+to\s+(hex|bin|oct|dec)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            yield break;

        var trimmed = query.Trim();

        // 1. Handle base conversion queries first
        var baseMatch = BaseConvRegex.Match(trimmed);
        if (baseMatch.Success)
        {
            var numPart = baseMatch.Groups[1].Value;
            var targetBase = baseMatch.Groups[3].Value.ToLowerInvariant();

            long val = 0;
            var parsed = false;
            double valDouble = 0;

            try
            {
                if (numPart.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    val = Convert.ToInt64(numPart, 16);
                    valDouble = val;
                    parsed = true;
                }
                else if (numPart.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                {
                    val = Convert.ToInt64(numPart[2..], 2);
                    valDouble = val;
                    parsed = true;
                }
                else
                {
                    // Invariant parse to match the rest of the provider (ScientificMathParser parses
                    // numbers invariantly); a culture-dependent parse would read "1.5" as 15 on de-DE.
                    if (double.TryParse(numPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out valDouble))
                    {
                        val = (long)valDouble;
                        parsed = true;
                    }
                }
            }
            catch { }

            if (parsed)
            {
                var convertedResult = string.Empty;
                var targetName = targetBase switch
                {
                    "hex" => TranslationService.Get("Calculator_Hex"),
                    "bin" => TranslationService.Get("Calculator_Bin"),
                    "oct" => TranslationService.Get("Calculator_Oct"),
                    "dec" => TranslationService.Get("Calculator_Dec"),
                    _ => string.Empty
                };

                if (targetBase == "hex")
                {
                    convertedResult = "0x" + Convert.ToString(val, 16).ToUpperInvariant();
                }
                else if (targetBase == "bin")
                {
                    convertedResult = "0b" + Convert.ToString(val, 2);
                }
                else if (targetBase == "oct")
                {
                    convertedResult = "0" + Convert.ToString(val, 8);
                }
                else if (targetBase == "dec")
                {
                    convertedResult = valDouble.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                var desc = TranslationService.Format("Calculator_BaseConv", numPart, targetName);

                if (!string.IsNullOrEmpty(convertedResult))
                {
                    yield return new InstantResultItem
                    {
                        Title = $"{numPart} = {convertedResult}",
                        Description = desc + TranslationService.Get("Calculator_DescSuffix"),
                        IconData = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-2 10h-4v4h-2v-4H7v-2h4V7h2v4h4v2z",
                        IconColor = "CalculatorIconColor",
                        ActionType = "Copy",
                        ActionArgument = convertedResult,
                        TabCompletion = convertedResult
                    };
                    yield break;
                }
            }
        }

        // 2. Handle scientific calculations
        if (!MathQueryRegex.IsMatch(trimmed))
            yield break;

        // Must contain at least one digit or a constant to be a valid expression
        var hasDigitOrConstant = trimmed.Any(char.IsDigit) ||
                                  trimmed.Contains("pi", StringComparison.OrdinalIgnoreCase) ||
                                  trimmed.Contains("e", StringComparison.OrdinalIgnoreCase) ||
                                  trimmed.Contains("π");
        if (!hasDigitOrConstant)
            yield break;

        // Prevent matching simple alphabetic text search keywords (e.g. "excel", "calculator" itself)
        // If the query is pure letters and is not a constant, skip
        if (trimmed.All(char.IsLetter) &&
            !string.Equals(trimmed, "pi", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(trimmed, "e", StringComparison.OrdinalIgnoreCase) &&
            trimmed != "π")
        {
            yield break;
        }

        double valCalculated;
        try
        {
            var parser = new ScientificMathParser(trimmed);
            valCalculated = parser.Parse();
        }
        catch
        {
            // Incomplete math expressions while typing
            yield break;
        }

        if (double.IsNaN(valCalculated) || double.IsInfinity(valCalculated))
            yield break;

        // Round nicely to avoid floating point precision garbage (e.g. 0.30000000000000004)
        string resultStr;
        if (valCalculated % 1 == 0)
        {
            resultStr = valCalculated.ToString("F0");
        }
        else
        {
            resultStr = Math.Round(valCalculated, 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (string.Equals(trimmed, resultStr, StringComparison.OrdinalIgnoreCase))
            yield break; // Don't show if result is identical to input

        yield return new InstantResultItem
        {
            Title = $"{trimmed} = {resultStr}",
            Description = TranslationService.Get("Calculator_FullResultDesc"),
            IconData = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-2 10h-4v4h-2v-4H7v-2h4V7h2v4h4v2z", // Beautiful plus/calculator icon
            IconColor = "CalculatorIconColor", // Calculator green
            ActionType = "Copy",
            ActionArgument = resultStr,
            TabCompletion = resultStr
        };
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return null;
        var trimmed = query.Trim();
        var mask = new bool[text.Length];

        if (text.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < trimmed.Length && i < mask.Length; i++)
            {
                mask[i] = true;
            }
            return mask;
        }

        var idx = text.IndexOf("=");
        if (idx > 0)
        {
            for (var i = 0; i < idx && i < mask.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                    mask[i] = true;
            }
            return mask;
        }

        return mask;
    }
}
