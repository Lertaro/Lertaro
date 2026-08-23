# Plugins

Lists every installed plugin, with the currently-loaded Plugin SDK version shown as a badge in the
page header — click it to open the [Developer Manual](../../dev-guide/), which is what that version
number is for.

The page is split into two panes: the installed plugins on the left, the selected one's details on
the right. They scroll independently, so a long plugin list and a long settings form don't drag each
other around.

If no plugins are installed, the page shows an empty-state message instead.

## Plugin list

One row per installed plugin, showing its name with its version underneath. Selecting a row opens
that plugin in the pane on the right.

The list leads with what there is to do: plugins with their own settings first, then plugins with
components you can switch off, then the rest, alphabetical within each group.

## Selected plugin

The pane opens on the plugin's icon, name, and **overall function description**.

Below that, a plugin that exposes its own configuration (custom settings beyond simple
enable/disable) gets two tabs, **Details** and **Configure**. A plugin with nothing to configure has
no tab strip at all — its details are the whole pane.

### Details

The components this plugin registers, grouped by type (search providers, dynamic menu providers,
etc.). Each toggleable component has its own **enable/disable checkbox**; a component marked as
required shows a lock icon instead and can't be turned off. Hovering the **(!)** beside a component
reveals its detailed function description.

When a group (or the plugin as a whole) has more than one toggleable component, a **Select All /
Deselect All** link appears next to its header, letting you flip every checkbox in that scope at
once instead of one at a time.

### Configure

The plugin's own settings, edited right here rather than in a separate dialog. A plugin that sorts
its settings into two or more groups gets its own row of tabs, one per group.

Nothing is written until you press **OK**. Leaving this tab — switching back to Details, or picking
a different plugin — rolls the fields back to their saved values, so edits you walked away from
can't be committed later by accident.

For a concrete example of what a plugin's configuration looks like in practice (e.g. changing a
trigger keyword), see [Instant Answers & Keyword Shortcuts](../instant-answers).

## Flow Launcher Plugin Support

In addition to native C# plugins built with `Lertaro.PluginSdk`, Lertaro includes built-in compatibility with the extensive Flow Launcher plugin ecosystem via the Flow Launcher Bridge.

- **Supported Plugin Types**: Flow Launcher plugins written in **C# (.NET)**, **Python 3.12** (with automated background `pip` dependency resolution from `requirements.txt`), **Node.js v20 LTS** (with automated background `npm` dependency resolution from `package.json`), or standalone **Executables** (`.exe`).
- **Installation**: Drop third-party Flow Launcher plugin folders into `<User Data Directory>\FlowData\Plugins\`. Lertaro automatically discovers, initializes, and integrates their query triggers and action keywords.
- **Isolated Runtimes**: Python (`FlowData\PythonEmbeded-{arch}`) and Node.js (`FlowData\NodeEmbeded-{arch}`) runtimes are completely self-contained within Lertaro's user data directory, downloaded on-demand and provisioned without polluting or modifying your system PATH.
- **Plugin Management & Configuration**: Type the `flow` keyword into the search bar to list all loaded Flow Launcher plugins along with their action keywords and status. Selecting any configurable plugin and pressing **Enter** opens its dedicated settings window, with full confirm-to-save and cancel-to-rollback protections.
