using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

// [DoNotParallelize]: the prompt tests wire PluginPromptService.PromptFunc, a shared static delegate, and
// reset it in finally -- without this another test class touching that same static could race them.
[TestClass]
[DoNotParallelize]
public sealed class MkdirActionTests
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

    private static readonly MkdirAction Action = new();

    [TestMethod]
    public void CanExecute_MultipleResults_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[]
        {
            new FakeResult { ContextDirectory = dir.Path },
            new FakeResult { ContextDirectory = dir.Path },
        };

        Assert.IsFalse(Action.CanExecute(results));
    }

    [TestMethod]
    public void CanExecute_NonExistentContextDirectory_ReturnsFalse()
    {
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = @"Z:\definitely-not-real-lertaro-dir" } };

        Assert.IsFalse(Action.CanExecute(results));
    }

    [TestMethod]
    public void CanExecute_RealContextDirectory_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path } };

        Assert.IsTrue(Action.CanExecute(results));
    }

    [TestMethod]
    public void Execute_CreatesDirectoryNamedByFullPath()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path, FullPath = "NewFolder" } };

        Action.Execute(results, null!);

        Assert.IsTrue(Directory.Exists(Path.Combine(dir.Path, "NewFolder")));
    }

    // The reported bug: a keyword-only query ("mkdir") lists the command with an empty argument, and
    // returning silently made Enter look broken -- the window closed and nothing was created. The missing
    // name is now asked for instead.
    [TestMethod]
    public void Execute_EmptyFullPath_PromptsAndCreatesThePromptedDirectory()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path, FullPath = "" } };
        PluginPromptService.PromptFunc = (title, fields, _) =>
        {
            Assert.HasCount(1, fields);
            Assert.AreEqual(ConfigFieldType.Text, fields[0].FieldType);
            Assert.IsTrue(fields[0].RequireNonEmpty);
            return new Dictionary<string, object?> { [fields[0].Key] = "PromptedFolder" };
        };
        try
        {
            Action.Execute(results, null!);

            Assert.IsTrue(Directory.Exists(Path.Combine(dir.Path, "PromptedFolder")));
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

            Assert.HasCount(0, Directory.GetDirectories(dir.Path));
        }
        finally
        {
            PluginPromptService.PromptFunc = null;
        }
    }

    // An explicitly typed name must not trigger a prompt at all -- the argument is the user's answer.
    [TestMethod]
    public void Execute_FullPathPresent_DoesNotPrompt()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path, FullPath = "Typed" } };
        var prompted = false;
        PluginPromptService.PromptFunc = (_, _, _) => { prompted = true; return null; };
        try
        {
            Action.Execute(results, null!);

            Assert.IsFalse(prompted);
            Assert.IsTrue(Directory.Exists(Path.Combine(dir.Path, "Typed")));
        }
        finally
        {
            PluginPromptService.PromptFunc = null;
        }
    }

    // No prompt service wired (an older host build) and an empty argument: nothing to do, and above all no
    // exception from a null prompt result.
    [TestMethod]
    public void Execute_EmptyFullPath_WithNoPromptService_DoesNothing()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path, FullPath = "" } };

        PluginPromptService.PromptFunc = null;
        Action.Execute(results, null!);

        Assert.HasCount(0, Directory.GetDirectories(dir.Path));
    }
}
