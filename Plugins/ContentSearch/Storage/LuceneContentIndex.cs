using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.NGram;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Lucene.Net 4.8-backed full-text index replacing the SQLite FTS5 trigram table on this
/// experiment branch. One document per indexed file, keyed by a path term so updates rewrite
/// in place; the text is stored so snippets come straight back without touching SQLite.
/// The analyzer is whitespace tokenization + lowercase + character n-grams (1..3), which keeps
/// the substring-search semantics of the FTS5 trigram tokenizer AND lets 1-2 character tokens
/// go through the index instead of a LIKE scan. Searchers are NRT readers over the writer, so
/// freshly written batches are searchable before commit.
/// </summary>
internal sealed class LuceneContentIndex : IDisposable
{
    private const string FieldPath = "path";
    private const string FieldContent = "content";

    private readonly IndexWriter _writer;
    private readonly NGramAnalyzer _analyzer = new();
    private readonly object _searchLock = new();
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;

    public LuceneContentIndex(string directoryPath)
    {
        System.IO.Directory.CreateDirectory(directoryPath);
        var directory = FSDirectory.Open(directoryPath);
        var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer);
        _writer = new IndexWriter(directory, config);
    }

    private sealed class NGramAnalyzer : Analyzer
    {
        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            var source = new WhitespaceTokenizer(LuceneVersion.LUCENE_48, reader);
            var lowered = new LowerCaseFilter(LuceneVersion.LUCENE_48, source);
            return new TokenStreamComponents(source, new NGramTokenFilter(LuceneVersion.LUCENE_48, lowered, 1, 3));
        }
    }

    public readonly record struct LuceneHit(string Path, string Content, float Score);

    /// <summary>
    /// Mirrors the write rule documented in DatabaseWriterHelper: source rows with text go in,
    /// a failed re-extraction drops the stale document, duplicates never enter (they reuse the
    /// source row's stored text at query time).
    /// </summary>
    public void ApplyBatch(IReadOnlyList<FileIndexBatchItem> items)
    {
        var failedPaths = new List<string>();
        foreach (var item in items)
        {
            if (item.ContentRef is not null) continue;
            if (string.IsNullOrWhiteSpace(item.Content)) failedPaths.Add(item.Path);
            else Upsert(item.Path, item.Content);
        }
        if (failedPaths.Count > 0) DeletePaths(failedPaths);
    }

    /// <summary>Inserts or rewrites one document; duplicates are never indexed (source rows only).</summary>
    public void Upsert(string path, string content)
    {
        var doc = new Document
        {
            new StringField(FieldPath, path, Field.Store.YES),
            new TextField(FieldContent, content, Field.Store.YES)
        };
        _writer.UpdateDocument(new Term(FieldPath, path), doc);
    }

    public void DeletePaths(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
            _writer.DeleteDocuments(new Term(FieldPath, path));
    }

    public void ClearAll()
    {
        _writer.DeleteAll();
        _writer.Commit();
    }

    /// <summary>
    /// Each whitespace-separated token must appear as a contiguous substring: the token is run
    /// through the same analyzer and its n-grams become a phrase query at the analyzer's own
    /// positions, so multi-token queries are AND-ed phrases just like the FTS5 quoted-token query.
    /// </summary>
    public IReadOnlyList<LuceneHit> Search(string rawQuery, int limit)
    {
        var query = BuildQuery(rawQuery);
        if (query == null)
            return Array.Empty<LuceneHit>();

        lock (_searchLock)
        {
            EnsureSearcher();
            var top = _searcher!.Search(query, limit);
            var hits = new List<LuceneHit>(top.ScoreDocs.Length);
            foreach (var scoreDoc in top.ScoreDocs)
            {
                var doc = _searcher.Doc(scoreDoc.Doc);
                hits.Add(new LuceneHit(doc.Get(FieldPath) ?? string.Empty, doc.Get(FieldContent) ?? string.Empty, scoreDoc.Score));
            }
            return hits;
        }
    }

    private Query? BuildQuery(string rawQuery)
    {
        var tokens = rawQuery.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return null;

        var root = new BooleanQuery();
        foreach (var token in tokens)
        {
            var phrase = new PhraseQuery();
            using var stream = _analyzer.GetTokenStream(FieldContent, token);
            var termAttr = stream.AddAttribute<ICharTermAttribute>();
            var posAttr = stream.AddAttribute<IPositionIncrementAttribute>();
            stream.Reset();

            var position = -1;
            while (stream.IncrementToken())
            {
                position += posAttr.PositionIncrement;
                phrase.Add(new Term(FieldContent, termAttr.ToString()), position);
            }
            stream.End();

            if (phrase.GetTerms().Length == 0)
                return null;
            root.Add(phrase, Occur.MUST);
        }
        return root;
    }

    private void EnsureSearcher()
    {
        if (_searcher == null)
        {
            _reader = DirectoryReader.Open(_writer, applyAllDeletes: true);
            _searcher = new IndexSearcher(_reader);
            return;
        }

        var reopened = DirectoryReader.OpenIfChanged(_reader, _writer, applyAllDeletes: true);
        if (reopened == null)
            return;

        _searcher = new IndexSearcher(reopened);
        _reader!.Dispose();
        _reader = reopened;
    }

    /// <summary>On-disk footprint of the Lucene index directory (all segment files).</summary>
    public long GetBytes()
    {
        var dir = _writer.Directory;
        long total = 0;
        foreach (var name in dir.ListAll())
        {
            try { total += dir.FileLength(name); }
            catch (FileNotFoundException) { /* segment deleted between listing and stat */ }
        }
        return total;
    }

    /// <summary>Makes pending writes durable. NRT readers see them before this; crash loses the batch.</summary>
    public void Commit() => _writer.Commit();

    public void Dispose()
    {
        _reader?.Dispose();
        _writer.Dispose();
        _analyzer.Dispose();
    }
}
