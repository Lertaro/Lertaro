using System.IO;
using Lertaro.Plugins.Calendar.Reminders;

namespace Lertaro.Plugins.Calendar.Tests.Reminders;

[TestClass]
public sealed class ReminderSoundTests
{
    private static readonly string Default = ReminderSound.DefaultPath;

    [TestMethod]
    public void DefaultPath_IsTheWindowsChimeThatShipsWithEveryInstall()
    {
        StringAssert.EndsWith(Default, ".wav", StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(File.Exists(Default), "the default sound is Windows' own, so it is always there");
        Assert.IsTrue(ReminderSound.IsPlayable(Default));
    }

    [TestMethod]
    public void Resolve_TurnedOffIsSilentWhateverIsConfigured()
    {
        using var files = new WavFiles();
        Assert.IsNull(ReminderSound.Resolve(false, files.Add("chime.wav")));
        Assert.IsNull(ReminderSound.Resolve(false, Default));
    }

    [TestMethod]
    public void Resolve_UsesTheConfiguredWav()
    {
        using var files = new WavFiles();
        var wav = files.Add("chime.wav");
        Assert.AreEqual(wav, ReminderSound.Resolve(true, wav));
    }

    [TestMethod]
    public void Resolve_AnEmptySettingMeansTheDefault()
    {
        Assert.AreEqual(Default, ReminderSound.Resolve(true, null));
        Assert.AreEqual(Default, ReminderSound.Resolve(true, "   "));
    }

    [TestMethod]
    public void Resolve_FallsBackToTheDefaultRatherThanGoingQuiet()
    {
        using var files = new WavFiles();

        // A format the player cannot open, a file that has since been deleted, and a path that is not
        // one at all: each is the settings page accepting what it cannot restrict, and silence here
        // would look exactly like the whole reminder feature being broken.
        Assert.AreEqual(Default, ReminderSound.Resolve(true, files.Add("song.mp3")));
        Assert.AreEqual(Default, ReminderSound.Resolve(true, Path.Combine(files.Root, "gone.wav")));
        Assert.AreEqual(Default, ReminderSound.Resolve(true, "not a path"));
    }

    [TestMethod]
    public void Resolve_ToleratesWhitespaceAndEnvironmentVariables()
    {
        using var files = new WavFiles();
        var wav = files.Add("chime.wav");

        Assert.AreEqual(wav, ReminderSound.Resolve(true, "  " + wav + "  "));
        Assert.AreEqual(Default, ReminderSound.Resolve(true, @"%WINDIR%\Media\Windows Notify Messaging.wav"),
            "a typed-in path may spell the Windows folder as a variable");
    }

    [TestMethod]
    public void IsPlayable_JudgesTheExtensionCaseInsensitively()
    {
        using var files = new WavFiles();
        Assert.IsTrue(ReminderSound.IsPlayable(files.Add("UPPER.WAV")));
        Assert.IsFalse(ReminderSound.IsPlayable(files.Add("song.wav.mp3")));
        Assert.IsFalse(ReminderSound.IsPlayable(string.Empty));
        Assert.IsFalse(ReminderSound.IsPlayable(null));
    }

    private sealed class WavFiles : IDisposable
    {
        internal WavFiles() => Root = Directory.CreateTempSubdirectory("lertaro-calendar-sound-").FullName;

        internal string Root { get; }

        /// <summary>Creates an empty file with that name and returns its full path.</summary>
        internal string Add(string name)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllBytes(path, []);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
