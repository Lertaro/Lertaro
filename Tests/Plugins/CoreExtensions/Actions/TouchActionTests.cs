using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

// [DoNotParallelize]: the prompt tests wire PluginPromptService.PromptFunc, a shared static delegate, and
// reset it in finally -- without this another test class touching that same static could race them.
[TestClass]
[DoNotParallelize]
public sealed class TouchActionTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static readonly TouchAction Action = new();

    [TestMethod]
    public void CanExecute_NonExistentContextDirectory_ReturnsFalse()
    {
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = @"Z:\definitely-not-real-lertaro-dir" } };

        Assert.IsFalse(Action.CanExecute(results));
    }

    [TestMethod]
    public void Execute_CreatesEmptyFileNamedByFullPath()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path, FullPath = "new.txt" } };

        Action.Execute(results, null!);

        var created = Path.Combine(dir.Path, "new.txt");
        Assert.IsTrue(File.Exists(created));
        Assert.AreEqual(0, new FileInfo(created).Length);
    }

    [TestMethod]
    public void Execute_NestedRelativePath_CreatesIntermediateDirectories()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path, FullPath = @"sub\new.txt" } };

        Action.Execute(results, null!);

        Assert.IsTrue(File.Exists(Path.Combine(dir.Path, "sub", "new.txt")));
    }

    [TestMethod]
    public void Execute_FileAlreadyExists_DoesNotOverwriteContent()
    {
        using var dir = new TempDirectory();
        var existing = Path.Combine(dir.Path, "existing.txt");
        File.WriteAllText(existing, "keep me");
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path, FullPath = "existing.txt" } };

        Action.Execute(results, null!);

        Assert.AreEqual("keep me", File.ReadAllText(existing));
    }

    // See MkdirActionTests: a keyword-only query leaves the argument empty, which used to make Enter look
    // like a no-op. The missing filename is asked for instead.
    [TestMethod]
    public void Execute_EmptyFullPath_PromptsAndCreatesThePromptedFile()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path, FullPath = "" } };
        PluginPromptService.PromptFunc = (_, fields, _) =>
        {
            Assert.HasCount(1, fields);
            Assert.IsTrue(fields[0].RequireNonEmpty);
            return new Dictionary<string, object?> { [fields[0].Key] = "prompted.txt" };
        };
        try
        {
            Action.Execute(results, null!);

            Assert.IsTrue(File.Exists(Path.Combine(dir.Path, "prompted.txt")));
        }
        finally
        {
            PluginPromptService.PromptFunc = null;
        }
    }

    [TestMethod]
    public void Execute_EmptyFullPath_Cancelled_CreatesNothing()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path, FullPath = "" } };
        PluginPromptService.PromptFunc = (_, _, _) => null; // user cancelled
        try
        {
            Action.Execute(results, null!);

            Assert.HasCount(0, Directory.GetFiles(dir.Path));
        }
        finally
        {
            PluginPromptService.PromptFunc = null;
        }
    }

    // An explicitly typed filename must not trigger a prompt.
    [TestMethod]
    public void Execute_FullPathPresent_DoesNotPrompt()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path, FullPath = "typed.txt" } };
        var prompted = false;
        PluginPromptService.PromptFunc = (_, _, _) => { prompted = true; return null; };
        try
        {
            Action.Execute(results, null!);

            Assert.IsFalse(prompted);
            Assert.IsTrue(File.Exists(Path.Combine(dir.Path, "typed.txt")));
        }
        finally
        {
            PluginPromptService.PromptFunc = null;
        }
    }

    [TestMethod]
    public void Execute_EmptyFullPath_WithNoPromptService_DoesNothing()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path, FullPath = "" } };

        PluginPromptService.PromptFunc = null;
        Action.Execute(results, null!);

        Assert.HasCount(0, Directory.GetFiles(dir.Path));
    }
}
