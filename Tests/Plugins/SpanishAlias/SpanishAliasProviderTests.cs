using Lertaro.PluginSdk.Abstractions.Plugins;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lertaro.Plugins.SpanishAlias.Tests;

[TestClass]
public sealed class SpanishAliasProviderTests
{
    [TestMethod]
    public void CanHandle_SpanishTextWithAccents_ReturnsTrue()
    {
        var provider = new SpanishAliasProvider();

        Assert.IsTrue(provider.CanHandle("Canción"));
        Assert.IsTrue(provider.CanHandle("Niño"));
        Assert.IsTrue(provider.CanHandle("Público"));
        Assert.IsTrue(provider.CanHandle("Cigüeña"));
        Assert.IsTrue(provider.CanHandle("Árgan"));
    }

    [TestMethod]
    public void CanHandle_PlainAscii_ReturnsFalse()
    {
        var provider = new SpanishAliasProvider();

        Assert.IsFalse(provider.CanHandle("Cancion"));
        Assert.IsFalse(provider.CanHandle("Hello World"));
        Assert.IsFalse(provider.CanHandle("12345"));
        Assert.IsFalse(provider.CanHandle(""));
    }

    [TestMethod]
    public void GetAliases_SpanishAccentedWords_ReturnsUnaccentedLowercaseAliases()
    {
        var provider = new SpanishAliasProvider();

        var aliasesCancion = provider.GetAliases("Canción").ToList();
        Assert.AreEqual(1, aliasesCancion.Count);
        Assert.AreEqual("cancion", aliasesCancion[0]);

        var aliasesNino = provider.GetAliases("Niño").ToList();
        Assert.AreEqual(1, aliasesNino.Count);
        Assert.AreEqual("nino", aliasesNino[0]);

        var aliasesPublico = provider.GetAliases("Público").ToList();
        Assert.AreEqual(1, aliasesPublico.Count);
        Assert.AreEqual("publico", aliasesPublico[0]);
    }

    [TestMethod]
    public void MapAliasToSourceIndices_ValidAlias_ReturnsIdentityArray()
    {
        var provider = new SpanishAliasProvider();

        var mapCancion = provider.MapAliasToSourceIndices("Canción.mp3", "cancion.mp3");
        Assert.IsNotNull(mapCancion);
        Assert.AreEqual(11, mapCancion.Length);
        for (var i = 0; i < mapCancion.Length; i++)
            Assert.AreEqual(i, mapCancion[i]);

        var mapCiguena = provider.MapAliasToSourceIndices("Cigüeña.png", "ciguena.png");
        Assert.IsNotNull(mapCiguena);
        Assert.AreEqual(11, mapCiguena.Length);
        for (var i = 0; i < mapCiguena.Length; i++)
            Assert.AreEqual(i, mapCiguena[i]);
    }

    [TestMethod]
    public void GetAliases_NameContainingAstralChar_GeneratesAliasWithoutThrowing()
    {
        // Regression: an astral char (the arrow U+1F872) reaches RemoveDiacritic one surrogate
        // half at a time; handing a lone surrogate to string.Normalize used to throw
        // ArgumentException on Windows ("String contains invalid Unicode code points") and
        // fail alias generation for the whole file name on every scan.
        var provider = new SpanishAliasProvider();

        var aliases = provider.GetAliases("BURIÁ \U0001F872 简体.srt").ToList();

        Assert.AreEqual(1, aliases.Count);
        Assert.AreEqual("buria \U0001F872 简体.srt", aliases[0]);
    }

    [TestMethod]
    public void GetAliasesUtf8_NameContainingAstralChar_EncodesAliasWithoutThrowing()
    {
        // The byte-native path walks the same per-char RemoveDiacritic.
        var provider = new SpanishAliasProvider();
        var sink = new AliasByteSink();

        provider.GetAliasesUtf8("BURIÁ \U0001F872 简体.srt", sink);

        Assert.AreEqual(1, sink.SegmentCount);
        Assert.AreEqual("buria \U0001F872 简体.srt", System.Text.Encoding.UTF8.GetString(sink.Segment(0)));
    }
}
