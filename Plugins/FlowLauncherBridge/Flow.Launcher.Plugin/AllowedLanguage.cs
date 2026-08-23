namespace Flow.Launcher.Plugin;

/// <summary>
/// Allowed plugin languages supported by Flow.Launcher.
/// </summary>
public static class AllowedLanguage
{
    public const string Python = "Python";
    public const string PythonV2 = "Python_v2";
    public const string CSharp = "CSharp";
    public const string FSharp = "FSharp";
    public const string Executable = "Executable";
    public const string ExecutableV2 = "Executable_V2";
    public const string TypeScript = "TypeScript";
    public const string TypeScriptV2 = "TypeScript_V2";
    public const string JavaScript = "JavaScript";
    public const string JavaScriptV2 = "JavaScript_V2";

    public static bool IsDotNet(string language)
    {
        return language.Equals(CSharp, StringComparison.OrdinalIgnoreCase)
            || language.Equals(FSharp, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPython(string language)
    {
        return language.Equals(Python, StringComparison.OrdinalIgnoreCase)
            || language.Equals(PythonV2, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNodeJs(string language)
    {
        return language.Equals(TypeScript, StringComparison.OrdinalIgnoreCase)
            || language.Equals(TypeScriptV2, StringComparison.OrdinalIgnoreCase)
            || language.Equals(JavaScript, StringComparison.OrdinalIgnoreCase)
            || language.Equals(JavaScriptV2, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExecutable(string language)
    {
        return language.Equals(Executable, StringComparison.OrdinalIgnoreCase)
            || language.Equals(ExecutableV2, StringComparison.OrdinalIgnoreCase);
    }
}
