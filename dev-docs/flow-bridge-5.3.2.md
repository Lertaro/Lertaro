**Flow Bridge 与 SDK 5.3.2 的同步**

Flow 兼容程序集从 5.3.1 更新为 5.3.2，桥插件从 1.3.5 更新为 1.3.6。对照上游提交 `58b799721a343811d8c42046cb2b4d63de1b3da6`，补齐最低应用版本、预览类型和可见性、SetGlyph，以及浏览器调用的第五个 extraArgs 参数。四参数浏览器方法仍保留，兼容已有编译产物。

Result 的默认 Preview 改为每个实例独立创建，Clone 复制预览记录并保留副标题高亮。JSON-RPC 结果也传递 Preview 和 PreviewVisibility。

Flow 的 AsyncAction 通过 PluginSdk 新增的异步回调传递到宿主，在 UI 上下文中 await；它优先于 Action，完成后按返回值决定是否隐藏窗口。结果行复用时同步更新异步回调和预览限制，文件型结果也执行其异步回调。

元数据预览支持图片、描述文字和自定义 PreviewPanel。Always 自动显示、Optional 跟随用户预览开关、Never 禁用预览。Markdown 内容显示为可选择的原文；当前宿主没有共用 Markdown 渲染器，此次未增加依赖。

验证包括上游公开成员名称/参数比对、5.3.1/5.3.2 程序集引用加载、旧新浏览器方法签名、Result 深复制、JSON-RPC 预览、真正挂起的异步动作、执行优先级及宿主映射。Flow Bridge 137 项测试通过；宿主相关定向测试通过。名称比对中剩余的 CultureInfo 项是完全限定类型名写法差异，实际方法已存在。
