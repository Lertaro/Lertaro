# Content Search

The Content Search plugin searches the text inside your local documents. It runs in the background and works alongside Lertaro's regular filename search.

## Getting started

Open **Settings → Plugins → Content Search → Configure**, set at least one monitored folder, then save. The default trigger keyword is `cs`:

```text
cs project plan
```

The keyword must be followed by a space. Replace `cs` with another keyword in the plugin settings if it conflicts with your workflow.

## What gets indexed

- **Monitored folders**: The plugin scans the configured local folders recursively. Environment variables such as `%USERPROFILE%` are supported.
- **File extensions**: The default list includes `txt`, `md`, `pdf`, `docx`, `docm`, `pptx`, `pptm`, `xlsx`, `xlsm`, and `csv`. You can change the comma-separated list to suit your files.
- **PDF files**: Searchable page text and saved values from fillable PDF form fields are included.
- **File size**: Files larger than the configured per-file limit are skipped. The content index also has a separate size cap; set it to `0` for unlimited size.
- **Excluded paths**: Add semicolon-separated regular expressions to exclude matching full paths. A match on a folder also excludes its contents.

Files are only searched after their text has been extracted successfully. Binary files without a suitable document extractor are skipped instead of being indexed as unreadable text.

## Searching

Type `cs` followed by a space and your keywords in the Quick Search Window. Matching files are shown with a text snippet and their containing folder; press `Enter` to open the selected file. When the query has results, the same file results are also available in the full search window when no type filter is active.

While the index is being built, the `cs` placeholder shows the number of indexed files and any remaining work. New and changed files are processed in the background, so regular filename search remains available during indexing.

## Clearing and rebuilding

The Content Search settings provide two separate actions:

- **Clear Index** removes the content index without scanning the monitored files again.
- **Rebuild Index** removes the content index and scans all monitored files again.

These actions affect only the Content Search index; Lertaro's normal filename index is not removed.

## Tips

- If a file does not appear, check its folder, extension, exclusion patterns, and size limit in the plugin settings.
- If you change the monitored folders or indexing rules, save the settings and allow the background scan to complete.
- Use **Rebuild Index** after changing rules when you want existing files to be extracted again.
