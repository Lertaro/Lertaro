using System.Collections.Concurrent;
using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Manages action keywords mapping and global plugin registrations for Flow.Launcher plugins.
/// Split out from FlowPluginHost to keep files modular and strictly under the line limit.
/// </summary>
public sealed class FlowPluginKeywordManager
{
    private readonly ConcurrentDictionary<string, List<PluginPair>> _keywordPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PluginPair> _globalPlugins = [];

    public IReadOnlyList<PluginPair> GlobalPlugins => _globalPlugins;
    public IReadOnlyDictionary<string, List<PluginPair>> KeywordPlugins => _keywordPlugins;

    public void RegisterPluginKeywords(PluginPair pair)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(pair.Metadata.ActionKeyword))
            keywords.Add(pair.Metadata.ActionKeyword);
        if (pair.Metadata.ActionKeywords != null)
        {
            foreach (var kw in pair.Metadata.ActionKeywords)
                if (!string.IsNullOrWhiteSpace(kw))
                    keywords.Add(kw);
        }

        if (keywords.Contains("*") || keywords.Count == 0)
        {
            lock (_globalPlugins)
            {
                if (!_globalPlugins.Contains(pair))
                    _globalPlugins.Add(pair);
            }
        }

        foreach (var kw in keywords)
        {
            if (kw == "*")
                continue;

            _keywordPlugins.AddOrUpdate(
                kw,
                _ => [pair],
                (_, list) => { lock (list) { if (!list.Contains(pair)) list.Add(pair); } return list; });
        }
    }

    public void UnregisterPluginKeywords(PluginPair pair)
    {
        lock (_globalPlugins) { _globalPlugins.Remove(pair); }
        foreach (var (key, list) in _keywordPlugins)
        {
            lock (list)
            {
                list.RemoveAll(p => string.Equals(p.Metadata.ID, pair.Metadata.ID, StringComparison.OrdinalIgnoreCase));
                if (list.Count == 0)
                {
                    _keywordPlugins.TryRemove(key, out _);
                }
            }
        }
    }

    public void AddActionKeyword(PluginPair pair, string newActionKeyword)
    {
        if (string.IsNullOrWhiteSpace(newActionKeyword)) return;
        if (!pair.Metadata.ActionKeywords.Contains(newActionKeyword, StringComparer.OrdinalIgnoreCase))
            pair.Metadata.ActionKeywords.Add(newActionKeyword);

        _keywordPlugins.AddOrUpdate(
            newActionKeyword,
            _ => [pair],
            (_, list) => { lock (list) { if (!list.Contains(pair)) list.Add(pair); } return list; });
    }

    public void RemoveActionKeyword(PluginPair pair, string oldActionKeyword)
    {
        if (string.IsNullOrWhiteSpace(oldActionKeyword)) return;
        pair.Metadata.ActionKeywords.RemoveAll(k => string.Equals(k, oldActionKeyword, StringComparison.OrdinalIgnoreCase));
        if (_keywordPlugins.TryGetValue(oldActionKeyword, out var list))
        {
            lock (list)
            {
                list.RemoveAll(p => string.Equals(p.Metadata.ID, pair.Metadata.ID, StringComparison.OrdinalIgnoreCase));
                if (list.Count == 0)
                {
                    _keywordPlugins.TryRemove(oldActionKeyword, out _);
                }
            }
        }
    }

    public bool ActionKeywordAssigned(string actionKeyword) =>
        !string.IsNullOrWhiteSpace(actionKeyword) &&
        _keywordPlugins.TryGetValue(actionKeyword, out var list) &&
        list.Count > 0;

    public void UpdateActionKeyword(PluginPair pair, string newActionKeyword)
    {
        if (string.IsNullOrWhiteSpace(newActionKeyword)) return;
        UnregisterPluginKeywords(pair);
        pair.Metadata.ActionKeyword = newActionKeyword;
        pair.Metadata.ActionKeywords.Clear();
        pair.Metadata.ActionKeywords.Add(newActionKeyword);
        RegisterPluginKeywords(pair);
    }

    public void Clear()
    {
        lock (_globalPlugins) { _globalPlugins.Clear(); }
        _keywordPlugins.Clear();
    }
}
