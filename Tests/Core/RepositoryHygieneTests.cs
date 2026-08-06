using System.Text.RegularExpressions;

namespace Lertaro.Core.Tests;

// Enforces repo rule 15 (no privacy-sensitive information in code, comments or tests) by scanning the
// working tree, rather than relying on it being remembered while writing.
//
// It exists because remembering did not work: real paths from live debugging reached two plugin test
// suites, were fixed, and the same class of mistake happened again -- personal folder names copied out
// of a screenshot into fixtures. This is a public repository with forks, so anything committed is
// effectively permanent; a test is the only thing that catches it before it is.
[TestClass]
public sealed class RepositoryHygieneTests
{
    private static readonly string[] SkipDirectories =
    {
        "obj", "bin", "node_modules", ".git", ".vs", ".claude", "scratch", "TestResults", "packages",
    };

    private static readonly string[] ScannedExtensions = { ".cs", ".xaml", ".csproj", ".slnx", ".json" };

    // Identifiers taken from the machine this runs on, rather than a list of names to avoid. A hardcoded
    // list would have to spell out the very things it is protecting -- putting them in the repository to
    // keep them out of it -- and would only ever cover whoever wrote the list. Reading them at runtime
    // means the check protects each contributor against their own paths leaking, and names nobody.
    private static IEnumerable<(string What, Regex Pattern)> MachineIdentifiers()
    {
        foreach (var (what, value) in new[]
        {
            ("the current Windows account name", Environment.UserName),
            ("this machine's name", Environment.MachineName),
        })
        {
            // Very short or obviously generic values would fire on ordinary code; they also are not
            // identifying, which is the thing being protected.
            if (string.IsNullOrWhiteSpace(value) || value.Length < 4)
                continue;
            if (value.Equals("user", StringComparison.OrdinalIgnoreCase) || value.Equals("test", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return (what, new Regex($@"\b{Regex.Escape(value)}\b", RegexOptions.IgnoreCase));
        }
    }

    // Placeholder drives the fixtures deliberately use. Anything else on a non-system drive is likely a
    // real path copied from a live session.
    private static readonly Regex NonSystemDrivePath =
        new(@"@?""(?<drive>[D-Zd-z]):\\(?<rest>[^""]*)""", RegexOptions.None);

    private static readonly string[] AllowedNonSystemRoots =
    {
        @"Z:\", @"z:\", @"T:\", @"D:\Projects", @"d:\projects", @"D:\", @"d:\",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return dir!.FullName;
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path);
            if (relative.Split(Path.DirectorySeparatorChar).Any(seg => SkipDirectories.Contains(seg, StringComparer.OrdinalIgnoreCase)))
                continue;
            if (!ScannedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                continue;
            if (IsBuildGeneratedProject(path))
                continue;
            yield return path;
        }
    }

    // Building a WPF project makes MSBuild write a temporary copy of the project file -- named
    // <Project>_<random>_wpftmp.csproj -- into the PROJECT directory rather than into obj, so the skip
    // list above never covered it. It is a generated file full of resolved absolute paths, NuGet package
    // locations among them, which is to say it names the account the build ran under.
    //
    // It exists only while a build is in flight, so this scan hit it exactly when it ran alongside one:
    // `dotnet test` on the whole test solution builds and tests projects concurrently by design. That
    // made this test fail intermittently, on 49 hits in a file nobody wrote and which is gone by the time
    // anyone looks -- the worst possible shape for a check whose entire value is being believed when it
    // does fire.
    private static bool IsBuildGeneratedProject(string path) =>
        Path.GetFileNameWithoutExtension(path).EndsWith("_wpftmp", StringComparison.OrdinalIgnoreCase);

    [TestMethod]
    public void NothingNamesTheMachineThisRunsOn()
    {
        var root = RepoRoot();
        var identifiers = MachineIdentifiers().ToArray();
        if (identifiers.Length == 0)
            return; // nothing distinctive enough to look for

        var hits = new List<string>();
        foreach (var file in SourceFiles(root))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (what, pattern) in identifiers)
                {
                    if (pattern.IsMatch(lines[i]))
                        hits.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: contains {what}");
                }
            }
        }

        Assert.IsEmpty(hits,
            "repo rule 15: substitute a placeholder (C:\\Users\\testuser\\..., MACHINE) before committing.\n" +
            string.Join("\n", hits));
    }

    [TestMethod]
    public void NoAbsolutePathsOnUnexpectedDrives()
    {
        var root = RepoRoot();
        var hits = new List<string>();

        foreach (var file in SourceFiles(root))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in NonSystemDrivePath.Matches(lines[i]))
                {
                    var literal = m.Value.Trim('@', '"');
                    if (AllowedNonSystemRoots.Any(a => literal.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    hits.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {literal}");
                }
            }
        }

        Assert.IsEmpty(hits,
            "repo rule 15: a path on a non-system drive is usually one copied from a live session.\n" +
            "If it is genuinely synthetic, move it onto one of the placeholder roots this test allows.\n" +
            string.Join("\n", hits));
    }
}
