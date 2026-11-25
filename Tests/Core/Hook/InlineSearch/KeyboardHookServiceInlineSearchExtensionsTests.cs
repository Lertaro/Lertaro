using Lertaro.Core.Hook;
using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
[DoNotParallelize]
public sealed class KeyboardHookServiceInlineSearchExtensionsTests
{
    [TestInitialize]
    public void Initialize() => UserSettings.ForceReload();

    [TestCleanup]
    public void Cleanup() => UserSettings.ForceReload();

    [TestMethod]
    [DataRow(0x10u)]
    [DataRow(0x12u)]
    public void HandleInlineSearchKeys_InjectedText_RemainsWithHost(uint flags)
    {
        using var service = new KeyboardHookService(new ExplorerTracker()) { IsInlineSearchVisible = true };
        var characters = 0;
        service.OnCharacterTyped += _ => characters++;

        Assert.IsFalse(service.HandleInlineSearchKeys(0x41,
            new KeyboardNativeMethods.KBDLLHOOKSTRUCT { vkCode = 0x41, flags = flags }, IntPtr.Zero));
        Assert.AreEqual(0, characters);
    }
}
