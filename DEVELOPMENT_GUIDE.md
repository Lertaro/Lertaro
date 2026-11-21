# Development Guide

These rules apply whenever an agent edits, builds, tests, or deploys this repository.

1. **On-Demand and Narrow-Scope Compilation**
    - Compile projects only when absolutely necessary.
    - Do not build the whole production solution blind (`dotnet build Lertaro.slnx`, `Lertaro.Plugins.slnx`). Build only the subproject the changed files belong to, for example `dotnet build Src/MySubProject.csproj`.
    - Building one project reduces turnaround time and file-lock conflicts.
    - **Full test suite**: During development, run only the affected test projects. Reserve the aggregate suite for the formal release process, after those projects pass.

2. **Do Not Terminate App and Service**
    - Do not stop the app or the service on your own. Building and testing do not conflict with them while they run.

    - Only when a binary/DLL lock conflict occurs during compilation leading to a build failure, and with explicit user authorization or instruction, you may release the locks using the following sequence:

        ```powershell
        # Stop the Lertaro Windows Service (requires Admin privileges)
        powershell -Command "Start-Process net -ArgumentList 'stop Lertaro.Service' -Verb RunAs -WindowStyle Hidden"
        # Force kill running processes
        taskkill /f /im Lertaro.App.exe
        taskkill /f /im Lertaro.Service.exe
        # Elevate taskkill to runas to kill if running with admin privileges
        powershell -Command "Start-Process taskkill -ArgumentList '/f /im Lertaro.App.exe' -Verb RunAs -WindowStyle Hidden"
        powershell -Command "Start-Process taskkill -ArgumentList '/f /im Lertaro.Service.exe' -Verb RunAs -WindowStyle Hidden"
        ```

3. **Code Formatting Before Committing & Authorized Git Actions**
    - Before committing any changes, you must run code formatting on the target project/solution to enforce code styles (configured in `.editorconfig`):

        ```powershell
        dotnet format style <PathToProjectOrSolution> --severity warn
        ```

    - **Only run formatting immediately before committing (not before every compilation) / 只在commit前才format**.

    - Never execute `git commit` or `git push` without explicit user authorization. All code changes must be reviewed and submitted under the direct instructions of the user.

    - **Commit Message Standard**: All commit messages must be written in **English**.

    - Use an English commit body when motivation or non-obvious design details need explanation.

    - Do not add AI attribution or assistant co-author trailers to commits or PR descriptions.

4. **Launch and Debug Workflows**
    - When launching the built executable, to prevent crashes due to incorrect current working directories, you must explicitly specify the output directory of the target binary as the working directory.

    - **For direct user local launching**, the recommended concise command is:

        ```powershell
        powershell -Command "Start-Process -FilePath '<AppOutputPath>\<AppName>.exe' -WorkingDirectory '<AppOutputPath>'"
        ```

    - **For the AI assistant launching in the background agent and requiring the process to keep running on the desktop**:

        ```powershell
        powershell -Command "Start-Process <AppOutputPath>\<AppName>.exe -WorkingDirectory <AppOutputPath> -Wait"
        ```

5. **Code File Line Limit (Modularization & Decoupling)**
    - Keep `.cs` and `.xaml` files under 300 lines where practical.
    - Before every compilation/build, check the line counts of the modified files. If any file exceeds 300 lines, evaluate whether it should be split. If the benefit of splitting is too low, leave it unsplit.
    - Do not use partial classes or partial views as a shortcut to bypass this limit. When splitting is warranted, perform structural decoupling by extracting clean helper classes, using C# extension methods, or grouping logical subcomponents into subfolders.

6. **Clean File Naming and Directory Namespace Hierarchy**
    - Do not create multi-dot source code files such as `Class.Helper.cs` or `Feature.Service.cs`.
    - Instead, strictly use subdirectories and nested namespace hierarchies (e.g., place the helper in `Feature/Helper.cs` and match it with the nested namespace `MyProject.Feature`). This keeps filenames simple, intuitive, and standard.

7. **App Versioning, Tagging, and Release Flow**
    - **Release-Only Version Bump**: Version numbers in `.csproj` files must **ONLY** be modified/bumped during the formal release process (Release Flow). Do **NOT** modify or bump version numbers during regular development, bug fixing, or feature modification tasks.
    - **Project Modification Detection**: During the release flow, check which subprojects (e.g., Core, App, Service) have been modified since the last release (last git tag). If a subproject has been modified, its version number must be bumped.
        - **Comments and documentation**: Comment-only or documentation-only changes do not require a subproject version bump. Inspect changed lines, not just filenames.
        - **New Plugin Exception**: For new plugins being released for the first time, their version number in their `.csproj` file should remain at `1.0.0` instead of being bumped.
        - **App Unconditional Bump Rule**: The `App` project version must **always** be bumped in every release, even if the App project itself has no direct code changes. Any modification to Core, Service, or any plugin constitutes a reason to bump the App version, since the App ships all components together.
    - **Version Bump Rule**: Locate `<Version>X.Y.Z</Version>` inside the modified project's `.csproj` file and bump it to the next version. The version number must increment in decimal format (where Y and Z are treated as a two-digit decimal number; hence, adding 1 to the last segment carries over to the middle segment when it reaches 9, e.g., `1.6.3` -\> `1.6.4`, `1.0.9` -\> `1.1.0`, or `1.6.9` -\> `1.7.0`).
    - When releasing a new version, follow these steps in order:
        1. Run code formatting (`dotnet format`) first, then commit all functional/code modifications.

        2. Bump the version of all modified projects (in their `.csproj` files).

        3. Commit the version bump change:

            ```bash
            git add <PathToProjectOrSolution>
            git commit -m "bump: version vX.Y.Z"
            ```

            Use the App version as the single version in this commit message and the release tag, even when several projects are bumped.

        4. Tag the commit with the version number (prefixed with `v`):

            ```bash
            git tag vX.Y.Z
            ```

        5. Push both the branch commits and the tag to the remote repository.

8. **No Lazy #pragma Warning Disables**
    - **Do NOT use `#pragma warning disable` / `#pragma warning restore` as a shortcut to ignore compiler warnings**.
    - Fix the warning in the code itself: null checks, pattern matching, explicit casts. Suppressing it is not a fix.
    - This applies to test code too: an MSTest analyzer warning (e.g. `MSTEST0037` suggesting `Assert.IsTrue`/`Assert.HasCount`/`Assert.IsGreaterThan` over a raw `Assert.AreEqual`/boolean comparison) gets fixed by using the suggested assertion, never suppressed.

9. **No Fallback Translation Strings in Code**
    - Translation lookups already return a visible `[Key]` placeholder for missing entries. Do not append hardcoded fallback strings to these non-nullable results.
    - If a translation key is missing from the locale JSON files, the fix is to add the key (in every locale the project ships), not to paper over it with an inline fallback string in the `.cs` file.

10. **Mandatory Test Coverage for New or Changed Testable Code**

- Whenever you write new code or modify existing code, if that code contains real logic worth testing (pure functions, branching/decision logic, algorithms, parsing, state derivation), you **must** add or update MSTest coverage for it as part of the same change --- do not wait to be asked separately. Tests live under `Tests/<ProjectName>/`, mirroring the production project's folder structure; a test project's `.csproj` is named after the production project it tests (e.g. `Tests/Core/Core.csproj`, not `Lertaro.Core.Tests.csproj`), with `AssemblyName`/`RootNamespace` set explicitly to `Lertaro.<ProjectName>.Tests`.
- Trivial property forwarding, pure UI/XAML glue, and OS-bound orchestration without an injectable seam do not need tests solely to increase coverage.
- When decisions depend on real I/O, extract the decision into a small pure method and test it. Leave logic untested only when that extraction is not practical.
- Prefer a small extraction to adding dependency-injection plumbing across several constructors. Use a larger DI refactor only when requested.
- This rule is forward-looking: it does not require retroactively covering pre-existing code you are not otherwise touching in the current change.

11. **Test Code Conventions & Infrastructure**
    - **Directory structure and namespaces mirror production 1:1**: a test for `App/ViewModels/Settings/Plugins/PluginConfigFieldViewModel.cs` lives at `Tests/App/ViewModels/Settings/Plugins/PluginConfigFieldViewModelTests.cs`. Its namespace is the production namespace with `Lertaro.<ProjectName>` replaced by `Lertaro.<ProjectName>.Tests` and every segment after that left unchanged (production `Lertaro.App.ViewModels.Settings.Plugins` → test `Lertaro.App.Tests.ViewModels.Settings.Plugins`). New test subfolders/namespace segments must appear the moment the matching production subfolder does --- do not flatten multiple production subfolders into one test file's namespace, and do not invent a test-side folder that has no production counterpart.
    - Test class naming: one `[TestClass]` named `<ProductionTypeName>Tests` per production type under test. When a single production file declares several small related types (as rule 6 already allows for tightly-coupled helpers), its test file may likewise declare one `[TestClass]` per type, keeping the same grouping the production file uses.
    - Give each new test project its own `MSTestSettings.cs` containing `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`.
    - When a test needs to reach an `internal` member, add `<InternalsVisibleTo Include="Lertaro.<ProjectName>.Tests" />` to the production project's `.csproj` --- do not widen the member to `public` just to make it testable.
    - **Production test seams**: Do not add test-only constructors, marker types, or mutable overrides to production classes. Use project-level `InternalsVisibleTo` for internal access; extract testable logic when isolation requires more changes.
    - **Shared state**: Test classes that access mutable statics, singletons, or PluginSdk delegates must use `[DoNotParallelize]` and reset state in `[TestInitialize]` and `[TestCleanup]`.
    - **WPF `FrameworkElement` construction in tests**: MSTest's default test threads are MTA; constructing any `System.Windows.*` `FrameworkElement` (`Grid`, `MenuItem`, `ContextMenu`, ...) throws `InvalidOperationException` unless the test method runs on an STA thread. Use `[StaTestMethod]` (see `Tests/App/StaTestMethodAttribute.cs`) instead of `[TestMethod]` for any test that constructs one, directly or indirectly.
    - **Fakes**: Use handwritten nested fakes/stubs implementing the required members. Do not add mocking libraries.
    - Read the actual constructors and method bodies before writing tests; verify construction requirements and member accessibility.
    - Build and run only the specific test project you changed (see rule 1 --- never blind-build the whole solution), and when a test fails, fix it based on the real failure output, not by re-guessing what the "correct" expected value should have been.
12. **Comment Language & Detailed Documentation**
    - All comments in `.cs` files must be written in **English**, including comments that explain or reference Chinese-language text, bug reports, or user-facing strings.
    - Explain non-obvious constraints, algorithms, and reasons for decisions. Keep comments focused on current behavior; use a concrete example when it clarifies an edge case.
13. **No Double-Hyphen in XML-Style Comments**
    - Do not use `--` inside `<!-- ... -->` comments in `.xaml` or `.csproj` files --- MSBuild/XAML parses a literal `--` inside an XML comment as a syntax error (`MC3000`/`MSB4025`). Use `:` or rephrase instead. This is a recurring mistake; double-check any comment you write in these file types before saving.
14. **How to Split a File (Composition, Naming, Subfolders)**
    - When rule 5 requires splitting a file, extract the cohesive piece into a separate class the original _holds a reference to and delegates to_ (composition) --- never a base class the original inherits from, and never `partial` (already banned by rule 5). The extracted class typically takes the owning instance as a constructor parameter when it needs to read/write the owner's state (e.g. `PluginConfigArrayFieldSupport(PluginConfigFieldViewModel field)`; a static helper instead takes the owner as a per-call parameter, e.g. `NetworkDrivePermissionsHelper.UpdateRowPermissions(NetworkDriveSettingsViewModel vm, ...)`).
    - Name the extracted class for its concern, ending in `Helper`/`Support`/a specific role noun (`NetworkDrivePermissionsHelper`, `NetworkDriveSummaryHelper`, `PluginConfigArrayFieldSupport`, `ActionsMenuNavigator`) --- never a generic `Utils`/`Common`/`Misc` grab-bag.
    - Add a short comment describing the extracted responsibility and any shared state. Explain the split when it clarifies a design constraint; avoid repeating file-length bookkeeping.
    - **Single overflowing class vs. topic subfolder**: one class alone crossing 300 lines gets one sibling helper file in the same folder. When a whole _feature area_ accumulates several related files (not just one overflowing class), group them into a topic-named subfolder with a matching nested namespace segment per rule 6 (e.g. `Settings/NetworkDrive/`, `Settings/LocalDrive/`, `Settings/Plugins/`, `Providers/InstantAnswers/`, `Providers/Filters/`, `Providers/Indexing/`). Subfolder names are the feature/topic, not the file type (no `Helpers/`/`Models/`-style grouping by kind).
    - This kind of split stays inside the existing project (App, Core, a given plugin) --- it is never a reason to carve off a new `.csproj`.
15. **No Privacy-Sensitive Information in Code, Comments, or Tests**
    - **Production code, comments, and test code/fixtures must never contain privacy-sensitive information** --- the developer's real Windows username, machine name, email address, personal file paths, or any other personally-identifying data --- even when it was copied verbatim from a real screenshot, log, or live debugging session. Use a generic placeholder instead (e.g. `C:\Users\testuser\Desktop`, not the developer's real profile path) in all three: the code itself, comments explaining it, and any test that exercises it.
    - Treat public commits as permanent disclosure: removing data from the origin does not remove copies already made in forks.
    - Before committing, scan any new or touched file for real paths/usernames/identifiers that slipped in from a live debugging session (screenshots, logs, pasted output) --- do not rely on remembering to substitute them as you write, verify at the end.
16. **Never Hard-Wrap a CJK Paragraph in Markdown**
    - **In `Site/**/\*.md`, a paragraph of Chinese or Japanese text goes on one line, however long.** Markdown joins the lines of a wrapped paragraph with a space when it renders, which is correct between Latin words and between a CJK character and a Latin one, but between two CJK characters it renders a space nobody wrote: `…专门负责文件索引。这`+`个拆分是有意为之的…`comes out as`…这 个拆分…`. Breaking a line is only safe where Latin sits on one side of the break.
    - Applies to `zh-CN`, `zh-HK`, `zh-TW` and `ja-JP`. `ko-KR`, `es-ES` and `en-US` are unaffected --- a break between two words there renders exactly the space those languages already want.
    - **Never put a line break directly beside a `**`or`\_`.\*\* Whether those delimiters open or close depends on the characters either side of them, and a line break counts as whitespace there, so a break next to one can silently turn the emphasis into literal asterisks in the output. Applies both to breaking a line there and to joining one.
    - **Write `[**text**](url)`, not `**[text](url)**`, when CJK or Hangul sits directly against it.** The outer form does not parse in that position and leaks literal `**` into the page --- the only construct found to break this way. Adding a space around the `**` also fixes it, but that is the space this rule exists to avoid.
    - Verify by building the site before and after (`npm run site:build` in `Site/`) and comparing the rendered output, not by reading the Markdown: the whole class of bug is invisible in the source. Count `<strong>`/`<em>` (must not move), literal `**` in the text (must not increase --- the exclusion-rule pages legitimately contain some, as glob wildcards), and matches of "CJK, space, CJK".
17. **Mandatory All-Locale Synchronization for Site Documentation (`Site/`)**
    - Whenever any Site documentation file (under `Site/`) is created, modified, or updated, **ALL supported site locales** (all language directories under `Site/` including `user-guide/` root and any future added locales) must be updated and kept in sync simultaneously.
    - Never update only one locale (e.g. `zh-CN`) and leave the others behind. Every change, feature addition, or troubleshooting note in documentation must be translated and synchronized across every locale directory that exists in `Site/`.
    - Always enforce Rule 16 (single un-wrapped lines for CJK/Japanese paragraphs) when editing Chinese or Japanese site pages.
    - Always verify by running `npm run site:build` in `Site/` after making any documentation changes.
18. **Mandatory All-Locale Synchronization for Plugin Translations (`Plugins/`)**
    - Whenever a plugin translation key, string, or locale file (under `Plugins/<PluginName>/Resources/Translations/`) is added, updated, or modified, **ALL supported plugin locales** (all locale directories present under `Resources/Translations/` for that plugin) must be updated in sync.
    - Never add or update a translation key in only one locale while leaving other locale JSON files missing the key.
    - Combine with Rule 9: No hardcoded fallback strings in `.cs` code --- when a new user-facing string is introduced in a plugin, add the translation key to all locale JSON files shipped by that plugin simultaneously.
