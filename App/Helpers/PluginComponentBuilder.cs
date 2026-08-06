using System.Reflection;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Registries;

using Lertaro.App.Services.Plugin;

using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.Core.SearchIndex;
using Lertaro.App.ViewModels.Settings.General;
namespace Lertaro.App.Helpers;

internal static class PluginComponentBuilder
{
    internal static List<PluginComponentViewModel> BuildComponents(IPlugin plugin, string dllName, PluginManager manager, HashSet<string> disabledSet)
    {
        var components = new List<PluginComponentViewModel>();
        var assembly = plugin.GetType().Assembly;

        foreach (var reg in manager.AllActions.Where(r => r.Plugin == plugin))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.Action, reg.Action.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.Action, reg.Action.DisplayName, !disabledSet.Contains(id), GetDescriptionWithFallback(reg.Action)));
        }

        AddAssemblyProviders(components, assembly, dllName, manager, disabledSet);
        return components;
    }

    internal static void AddAssemblyProviders(List<PluginComponentViewModel> components, Assembly assembly, string dllName, PluginManager manager, HashSet<string> disabledSet)
    {
        foreach (var prov in AliasProviderRegistry.GetAllProviders().Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.AliasProvider, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.AliasProvider, string.IsNullOrWhiteSpace(prov.Name) ? prov.GetType().Name : prov.Name, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
        }
        foreach (var prov in ActivePathCollectorRegistry.GetAllCollectors().Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.ActivePathCollector, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.ActivePathCollector, string.IsNullOrWhiteSpace(prov.Name) ? prov.GetType().Name : prov.Name, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
        }
        foreach (var prov in FileDialogAdapterRegistry.GetAllAdapters().Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.FileDialogAdapter, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.FileDialogAdapter, string.IsNullOrWhiteSpace(prov.Name) ? prov.GetType().Name : prov.Name, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
        }
        foreach (var prov in InlineSearchAdapterRegistry.GetAllAdapters().Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.InlineSearchAdapter, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.InlineSearchAdapter, string.IsNullOrWhiteSpace(prov.Name) ? prov.GetType().Name : prov.Name, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
        }
        foreach (var prov in manager.AllInstantResultProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.InstantProvider, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.InstantProvider, prov.Name, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
        }
        foreach (var prov in manager.AllSearchableItemProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.SearchableItemProvider, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.SearchableItemProvider, prov.Name, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
        }
        foreach (var prov in manager.AllDynamicActionProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.DynamicActionProvider, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.DynamicActionProvider, prov.GroupName, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
        }
        foreach (var prov in manager.AllQuickNavigationProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.QuickNavigationProvider, prov.GetType().Name);
            var displayName = TranslationService.Get("Plugins_Comp_QuickNavigationProvider");
            components.Add(new PluginComponentViewModel(id, PluginComponentType.QuickNavigationProvider, displayName, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
        }
        foreach (var prov in manager.AllSidebarFilterProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var index = 0;
            foreach (var group in prov.GetFilterGroups())
            {
                var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.FilterProvider, $"{prov.GetType().Name}_{index}");
                components.Add(new PluginComponentViewModel(id, PluginComponentType.FilterProvider, group.Header, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
                index++;
            }
        }
        foreach (var prov in manager.AllResultColumnProviders.Where(p => p.GetType().Assembly == assembly))
        {
            foreach (var col in prov.GetColumns())
            {
                var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.ColumnProvider, col.ColumnId);
                components.Add(new PluginComponentViewModel(id, PluginComponentType.ColumnProvider, col.HeaderText, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
            }
        }
        foreach (var prov in manager.AllFilePreviewProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.FilePreviewProvider, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.FilePreviewProvider, prov.Name, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
        }
        foreach (var prov in manager.AllThumbnailProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.ThumbnailProvider, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.ThumbnailProvider, prov.Name, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
        }
        foreach (var prov in manager.AllQueryTokenProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.QueryTokenProvider, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.QueryTokenProvider, prov.Name, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
        }
        foreach (var prov in manager.AllQuickPanelTabProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.QuickPanelTabProvider, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.QuickPanelTabProvider, prov.Name, !disabledSet.Contains(id), GetDescriptionWithFallback(prov)));
        }
        foreach (var prov in manager.AllTranslationProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.TranslationProvider, prov.GetType().Name);
            var displayName = prov.SupportedCultures.Count > 0
                ? string.Join(", ", prov.SupportedCultures.Select(LanguageOption.GetLanguageDisplayName))
                : prov.Name;
            components.Add(new PluginComponentViewModel(id, PluginComponentType.TranslationProvider, displayName, true, GetDescriptionWithFallback(prov)));
        }
        foreach (var prov in manager.AllThemeProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = PluginLoaderHelper.MakeId(dllName, PluginComponentType.ThemeProvider, prov.GetType().Name);
            var themes = prov.GetThemes().ToList();
            var displayName = themes.Count > 0
                ? string.Join(", ", themes.Select(t => t.DisplayName))
                : prov.Name;
            components.Add(new PluginComponentViewModel(id, PluginComponentType.ThemeProvider, displayName, true, GetDescriptionWithFallback(prov)));
        }
    }

    internal static string GetDescriptionWithFallback(IPluginComponent component)
    {
        var desc = component.Description;
        if (!string.IsNullOrWhiteSpace(desc))
        {
            return desc;
        }

        var className = component.GetType().Name;
        var specificKey = $"Plugin_Comp_Desc_{className}";
        var val = TranslationService.Get(specificKey);
        if (val != $"[{specificKey}]")
        {
            return val;
        }

        if (component is ISearchResultAction) return TranslationService.Get("Plugins_TypeDesc_ISearchResultAction");
        if (component is IInstantResultProvider) return TranslationService.Get("Plugins_TypeDesc_IInstantResultProvider");
        if (component is ISearchableItemProvider) return TranslationService.Get("Plugins_TypeDesc_ISearchableItemProvider");
        if (component is IActivePathCollector) return TranslationService.Get("Plugins_TypeDesc_IActivePathCollector");
        if (component is IInlineSearchAdapter) return TranslationService.Get("Plugins_TypeDesc_IInlineSearchAdapter");
        if (component is IFilePreviewProvider) return TranslationService.Get("Plugins_TypeDesc_IFilePreviewProvider");
        if (component is IQueryTokenProvider) return TranslationService.Get("Plugins_TypeDesc_IQueryTokenProvider");
        if (component is ITranslationProvider) return TranslationService.Get("Plugins_TypeDesc_ITranslationProvider");
        if (component is IThemeProvider) return TranslationService.Get("Plugins_TypeDesc_IThemeProvider");
        if (component is IThumbnailProvider) return TranslationService.Get("Plugins_TypeDesc_IThumbnailProvider");
        if (component is IQuickNavigationProvider) return TranslationService.Get("Plugins_TypeDesc_IQuickNavigationProvider");
        if (component is IResultColumnProvider) return TranslationService.Get("Plugins_TypeDesc_IResultColumnProvider");
        if (component is ISidebarFilterProvider) return TranslationService.Get("Plugins_TypeDesc_ISidebarFilterProvider");
        if (component is IQuickPanelTabProvider) return TranslationService.Get("Plugins_TypeDesc_IQuickPanelTabProvider");
        if (component is IFileDialogAdapter) return TranslationService.Get("Plugins_TypeDesc_IFileDialogAdapter");
        if (component is IAliasProvider) return TranslationService.Get("Plugins_TypeDesc_IAliasProvider");
        if (component is IDynamicActionProvider) return TranslationService.Get("Plugins_TypeDesc_IDynamicActionProvider");

        return string.Empty;
    }
}
