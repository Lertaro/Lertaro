using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Lertaro.App.Helpers;

[assembly: AssemblyMetadata("Lertaro.Plugin.Name", "Literal plugin name")]
[assembly: AssemblyMetadata("Lertaro.Plugin.NameKey", "Missing_Translation_Key")]
[assembly: AssemblyMetadata("Lertaro.Plugin.Description", "Literal plugin description")]

namespace Lertaro.App.Tests.Helpers;

[TestClass]
[DoNotParallelize]
public sealed class PluginMetadataReaderTests
{
    private static string PluginPath => Path.Combine(AppContext.BaseDirectory, "Lertaro.Plugins.SystemSettings.dll");

    [TestMethod]
    public void Read_MissingTranslation_UsesDeclaredLiteralMetadata()
    {
        var metadata = PluginMetadataReader.Read(typeof(PluginMetadataReaderTests).Assembly.Location, "en-US");
        Assert.AreEqual("Literal plugin name", metadata.Name);
        Assert.AreEqual("Literal plugin description", metadata.Description);
    }

    [TestMethod]
    public void Read_InvalidResourceLength_DoesNotReadOutsideResourceDirectory()
    {
        var root = Directory.CreateTempSubdirectory("Lertaro-metadata-");
        try
        {
            var bytes = File.ReadAllBytes(PluginPath);
            using (var pe = new PEReader(new MemoryStream(bytes)))
            {
                var reader = pe.GetMetadataReader();
                var resource = reader.ManifestResources.Select(reader.GetManifestResource)
                    .First(r => reader.GetString(r.Name).Contains(".en_US.", StringComparison.Ordinal)
                        || reader.GetString(r.Name).Contains(".en-US.", StringComparison.Ordinal));
                var rva = pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress;
                var section = pe.PEHeaders.SectionHeaders[pe.PEHeaders.GetContainingSectionIndex(rva)];
                var offset = section.PointerToRawData + rva - section.VirtualAddress + (int)resource.Offset;
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), int.MaxValue);
            }
            var path = Path.Combine(root.FullName, "invalid-resource.dll");
            File.WriteAllBytes(path, bytes);
            Assert.AreEqual("invalid-resource", PluginMetadataReader.Read(path, "en-US").Name);
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void Read_AssemblyWithoutPluginMetadataOrSdk_UsesStandardAssemblyTitleAndVersion()
    {
        var assembly = typeof(Enumerable).Assembly;
        var metadata = PluginMetadataReader.Read(assembly.Location, "en-US");
        Assert.AreEqual(assembly.GetCustomAttribute<AssemblyTitleAttribute>()!.Title, metadata.Name);
        Assert.AreEqual(assembly.GetName().Version!.ToString(3), metadata.Version);
        Assert.AreEqual("", metadata.SdkVersion);
    }

    [TestMethod]
    [DataRow("zh-CN", "系统设置搜索", "系统设置快速直达插件。")]
    [DataRow("en-US", "System Settings Search", "System settings shortcut plugin.")]
    [DataRow("missing-culture", "System Settings Search", "System settings shortcut plugin.")]
    public void Read_UnloadedAssembly_ReturnsLocalizedMetadataWithoutLoadingCode(string culture, string name, string descriptionPrefix)
    {
        var loadedBefore = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.FullName).ToArray();

        var metadata = PluginMetadataReader.Read(PluginPath, culture);

        Assert.AreEqual(name, metadata.Name);
        Assert.StartsWith(descriptionPrefix, metadata.Description);
        Assert.AreEqual(AssemblyName.GetAssemblyName(PluginPath).Version!.ToString(3), metadata.Version);
        Assert.IsFalse(string.IsNullOrWhiteSpace(metadata.SdkVersion));
        var pluginIdentity = AssemblyName.GetAssemblyName(PluginPath).FullName;
        Assert.AreEqual(loadedBefore.Contains(pluginIdentity), AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName == pluginIdentity));
    }

    [TestMethod]
    public void Read_RenamedDll_UsesMetadataInsteadOfFilename()
    {
        var root = Directory.CreateTempSubdirectory("Lertaro-metadata-");
        try
        {
            var path = Path.Combine(root.FullName, "renamed.dll");
            File.Copy(PluginPath, path);
            Assert.AreEqual("系统设置搜索", PluginMetadataReader.Read(path, "zh-CN").Name);
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void Read_InvalidDll_PreservesManageableFallback()
    {
        var root = Directory.CreateTempSubdirectory("Lertaro-metadata-");
        try
        {
            var path = Path.Combine(root.FullName, "invalid.dll");
            File.WriteAllText(path, "not an assembly");
            var metadata = PluginMetadataReader.Read(path, "zh-CN");
            Assert.AreEqual("invalid", metadata.Name);
            Assert.AreEqual("", metadata.Version);
            Assert.AreEqual("", metadata.Description);
        }
        finally { root.Delete(true); }
    }
}
