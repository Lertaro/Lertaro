# 界面与预览扩展

本章节介绍 `Lertaro.PluginSdk` 中用于深度扩展主界面侧边栏、表格自定义数据列、快速面板动态工作区标签、QuickLook 自定义文件预览器、缩略图提取、WPF 资源字典主题包以及多语言本地化的 UI 扩展接口。

## 1. 侧边栏筛选分类 `ISidebarFilterProvider`

用于在主搜索窗口的左侧侧边栏中注入自定义的分类筛选树：

```csharp
namespace Lertaro.PluginSdk;

public interface ISidebarFilterProvider : IPluginComponent
{
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}

public sealed class SidebarFilterGroup
{
    public required string GroupName { get; init; }
    public required IReadOnlyList<SidebarFilterItem> FilterItems { get; init; }
}

public sealed class SidebarFilterItem
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public ImageSource? Icon { get; init; }
    public required Func<ISearchResult, bool> FilterFunc { get; init; } // 命中判断委托
}
```

`SidebarFilterGroup.Id` 是可选的稳定分组标识。宿主可以根据 `Type` 等已知标识应用内置行为；如果分组完全由插件自定义，则留空即可。

## 2. 结果表格自定义数据列 `IResultColumnProvider`

在主搜索窗口的“详细信息”多列表格视图中追加自定义数据列（例如：提取并展示音视频时长、代码行数或 Git 仓库分支名）：

```csharp
public interface IResultColumnProvider : IPluginComponent
{
    string ColumnId { get; }
    string HeaderText { get; }
    double DefaultWidth => 120;
    double MinWidth => 40;
    bool IsVisibleByDefault => false;
    string? GetCellText(ISearchResult result);
    int Compare(ISearchResult a, ISearchResult b) => 0; // 点击表头时的排序列排序规则
}
```

## 3. 快速面板动态标签页 `IQuickPanelTabProvider`

为居中快速浮窗底部的[**快速面板**](../../user-guide/settings/quick-panel)提供动态工作区标签页：

```csharp
public interface IQuickPanelTabProvider : IPluginComponent
{
    string TabId { get; }
    string Title { get; }
    string? IconPath => null;
    Task<IReadOnlyList<ISearchResult>> GetItemsAsync(CancellationToken token);

    // 拖拽文件/链接移入该标签页时的接收逻辑
    bool CanHandleDragOver(IDataObject data) => false;
    Task HandleDropAsync(IDataObject data, CancellationToken token) => Task.CompletedTask;

    // 是否支持用户手动拖拽调整条目顺序
    bool SupportsReorder => false;
    Task SaveOrderAsync(IReadOnlyList<ISearchResult> orderedItems) => Task.CompletedTask;

    // 自定义该标签页专用的上下文动作菜单上下文
    DynamicActionContext CreateActionContext() => DynamicActionContext.Default;
}
```

## 4. 文件即时预览与缩略图

### 自定义文件预览器 `IFilePreviewProvider`

接管并自定义特定文件类型在 QuickLook（空格键预览）浮窗中的可视化渲染逻辑：

```csharp
public interface IFilePreviewProvider : IPluginComponent
{
    bool CanPreview(string filePath);
    int Priority => 0;                  // 多插件冲突时的仲裁优先级（值越大越优先）
    FrameworkElement CreatePreviewControl(string filePath);
}
```

#### 预览生命周期与复用优化契约

若插件返回的 WPF `FrameworkElement` 实现了以下可选契约，宿主会在预览生命周期内执行高级优化：

- **`IPreviewSessionAware`**：实现 `void OnPreviewClosed()`，在用户关闭预览窗口或切换到其他不匹配的文件时触发，用于安全释放音视频播放器句柄、WebView2 实例或大文件流。
- **`IReusablePreview`**：实现 `void UpdatePreview(string filePath)`。当用户按下上下箭头连续在同类文件间切换时，宿主不会销毁并重建控件，而是直接调用此方法就地更新内容，消除界面白屏与闪烁。

### 自定义缩略图提取器 `IThumbnailProvider`

为未安装系统 Shell 缩略图扩展的专有文件格式（如 `.blend`、`.psd`、`.dwg`）提取高分辨率缩略图：

```csharp
public interface IThumbnailProvider : IPluginComponent
{
    bool CanProvide(string filePath);
    Task<ImageSource?> GetThumbnailAsync(string filePath, int targetSize, CancellationToken token);
}
```

## 5. 外观主题与多语言

### 自定义主题包 `IThemeProvider`

为 Lertaro 贡献自定义的色彩方案与 WPF 资源字典：

```csharp
public interface IThemeProvider : IPluginComponent
{
    string ThemeId { get; }
    string DisplayName { get; }
    ResourceDictionary GetResourceDictionary(bool isDark);
}
```

### 多语言本地化 `ITranslationProvider`

为插件自身及宿主贡献动态多语言键值对：

```csharp
public interface ITranslationProvider : IPluginComponent
{
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```
