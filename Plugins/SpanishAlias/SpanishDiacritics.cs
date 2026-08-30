namespace Lertaro.Plugins.SpanishAlias;

public static class SpanishDiacritics
{
    public static bool IsSpanishDiacritic(char c) => c switch
    {
        'á' or 'Á' or 'é' or 'É' or 'í' or 'Í' or 'ó' or 'Ó' or 'ú' or 'Ú' or 'ü' or 'Ü' or 'ñ' or 'Ñ' => true,
        _ => false
    };

    public static char RemoveDiacritic(char c) => c switch
    {
        'á' or 'Á' => 'a',
        'é' or 'É' => 'e',
        'í' or 'Í' => 'i',
        'ó' or 'Ó' => 'o',
        'ú' or 'Ú' => 'u',
        'ü' or 'Ü' => 'u',
        'ñ' or 'Ñ' => 'n',
        _ => c <= 127 ? char.ToLowerInvariant(c) : RemoveUnicodeDiacritic(c)
    };

    private static char RemoveUnicodeDiacritic(char c)
    {
        // Surrogate halves of emoji and other astral chars reach here one char at a time (the
        // callers iterate char by char); handing a lone surrogate to Normalize throws
        // ArgumentException on Windows ("String contains invalid Unicode code points"). No Latin
        // diacritic lives inside a surrogate, so pass it through unchanged.
        if (char.IsSurrogate(c))
            return c;

        var normalized = c.ToString().Normalize(System.Text.NormalizationForm.FormD);
        return char.ToLowerInvariant(normalized[0]);
    }
}
