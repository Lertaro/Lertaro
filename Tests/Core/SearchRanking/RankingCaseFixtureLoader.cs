using System.Text.Json;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchRanking;

internal sealed record ReportedCorpusRow(
    string Token,
    string DisplayName,
    string Role,
    bool? ExpectedAcceptanceBySharedTightnessGate);

internal sealed record ReportedCaseSummary(
    string Id,
    string Window,
    IReadOnlyList<string?> Queries,
    IReadOnlyList<string> SyntheticPaths,
    IReadOnlyList<ReportedCorpusRow> Corpus);

internal static class RankingCaseFixtureLoader
{
    public static IReadOnlyList<ReportedCaseSummary> LoadReportedCases(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        RequireVersion(root, 1);
        var cases = new List<ReportedCaseSummary>();
        foreach (var item in Required(root, "cases").EnumerateArray())
        {
            var queries = Required(item, "queryVariants").EnumerateArray()
                .Select(query => OptionalString(query, "raw"))
                .ToArray();
            var corpusElements = Required(item, "corpus").EnumerateArray().ToArray();
            var paths = corpusElements
                .Select(candidate => RequiredString(candidate, "syntheticPath"))
                .ToArray();
            var corpus = corpusElements
                .Select(candidate => new ReportedCorpusRow(
                    RequiredString(candidate, "token"),
                    RequiredString(candidate, "displayName"),
                    RequiredString(candidate, "role"),
                    OptionalNullableBool(candidate, "expectedAcceptanceBySharedTightnessGate")))
                .ToArray();
            cases.Add(new ReportedCaseSummary(
                RequiredString(item, "id"),
                RequiredString(item, "window"),
                queries,
                paths,
                corpus));
        }
        return cases;
    }

    public static IReadOnlyList<RankingCase> LoadReplayCases(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        RequireVersion(root, 1);
        var cases = new List<RankingCase>();
        foreach (var item in Required(root, "cases").EnumerateArray())
        {
            var candidates = Required(item, "candidates").EnumerateArray()
                .Select(ParseCandidate)
                .ToArray();
            ValidateCandidates(candidates);
            var assertions = Required(item, "assertions");
            var rankingCase = new RankingCase(
                RequiredString(item, "id"),
                RequiredString(item, "window"),
                RequiredString(item, "rawQuery"),
                RequiredBool(item, "fuzzyEnabled"),
                Required(item, "typeOrder").EnumerateArray().Select(value => value.GetString()!).ToArray(),
                RequiredString(item, "providerReadiness"),
                candidates,
                Required(assertions, "mustAppearBefore").EnumerateArray()
                    .Select(pair => new ExpectedPair(
                        RequiredString(pair, "before"),
                        RequiredString(pair, "after")))
                    .ToArray(),
                Required(assertions, "mustBeTopK").EnumerateArray()
                    .Select(expectation => new TopKExpectation(
                        RequiredString(expectation, "token"),
                        RequiredInt(expectation, "k")))
                    .ToArray(),
                Required(assertions, "mustBeAbsent").EnumerateArray()
                    .Select(value => value.GetString()!)
                    .ToHashSet(StringComparer.Ordinal));
            ValidateAssertions(rankingCase);
            cases.Add(rankingCase);
        }
        return cases;
    }

    private static CandidateSnapshot ParseCandidate(JsonElement item)
    {
        var match = Required(item, "match");
        var kind = CandidateKindFlags.None;
        foreach (var value in Required(item, "kind").EnumerateArray())
            kind |= Enum.Parse<CandidateKindFlags>(value.GetString()!, ignoreCase: true);

        return new CandidateSnapshot(
            RequiredString(item, "token"),
            RequiredString(item, "displayName"),
            RequiredString(item, "syntheticPath"),
            kind,
            Enum.Parse<CandidateSource>(RequiredString(item, "source"), ignoreCase: true),
            new MatchRank(
                RequiredInt(match, "tier"),
                RequiredInt(match, "start"),
                RequiredDouble(match, "coverage"),
                RequiredDouble(match, "contiguity"),
                RequiredInt(match, "shape")),
            Enum.Parse<IndexRuleEffect>(RequiredString(item, "priority"), ignoreCase: true),
            RequiredBool(item, "alwaysTop"),
            RequiredInt(item, "historyScoreX100"),
            RequiredInt(item, "typeRank"),
            RequiredInt(item, "relevance"),
            OptionalBool(item, "isFavorite"),
            OptionalBool(item, "isHistoryOnly"),
            (ulong)OptionalInt(item, "engineTie"),
            OptionalEnum(item, "evidenceField", MatchEvidenceField.LegacyUnknown),
            OptionalEnum(item, "queryVariant", MatchQueryVariant.Original),
            OptionalBool(item, "explicitUserPriority"),
            OptionalIntOrDefault(item, "spanStart", -1),
            OptionalInt(item, "spanLength"),
            OptionalGrade(item, "expectedV3Grade"));
    }

    // Grades are named, not numbered: the docs and the enum disagree about whether ExactIdentity is 0 or 1,
    // and a fixture that silently means the other scale would assert the wrong ladder.
    private static MatchEvidenceGrade? OptionalGrade(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        var grade = Enum.Parse<MatchEvidenceGrade>(value.GetString()!, ignoreCase: true);
        if (grade == MatchEvidenceGrade.LegacyUnknown)
            throw new InvalidDataException($"'{name}' must name a real grade, not {grade}.");
        return grade;
    }

    private static T OptionalEnum<T>(JsonElement parent, string name, T fallback) where T : struct, Enum =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? Enum.Parse<T>(value.GetString()!, ignoreCase: true)
            : fallback;

    private static int OptionalIntOrDefault(JsonElement parent, string name, int fallback) =>
        parent.TryGetProperty(name, out var value) ? value.GetInt32() : fallback;

    private static void ValidateCandidates(IReadOnlyList<CandidateSnapshot> candidates)
    {
        if (candidates.Count == 0)
            throw new InvalidDataException("A ranking case must contain candidates.");
        if (candidates.Select(candidate => candidate.Token).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
            throw new InvalidDataException("Candidate tokens must be unique inside one ranking case.");
        foreach (var candidate in candidates)
        {
            if (!candidate.SyntheticPath.StartsWith(@"X:\Fixture\", StringComparison.Ordinal))
                throw new InvalidDataException($"Candidate '{candidate.Token}' does not use a synthetic fixture path.");
            if (candidate.DisplayName.Contains(@"C:\Users\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Candidate '{candidate.Token}' contains a user path in its display name.");
        }
    }

    // A case whose expectations name tokens that are not candidates cannot fail for the wrong reason: an
    // unknown token used to read as an order inversion. Absent-only cases are the exception, because the
    // point of #440 is that the row must never reach the candidate set at all.
    private static void ValidateAssertions(RankingCase rankingCase)
    {
        if (rankingCase.MustBeTopK.Count == 0
            && rankingCase.MustAppearBefore.Count == 0
            && rankingCase.MustBeAbsent.Count == 0)
        {
            throw new InvalidDataException($"Case '{rankingCase.Id}' declares no assertion.");
        }

        var tokens = rankingCase.Candidates.Select(candidate => candidate.Token).ToHashSet(StringComparer.Ordinal);
        var expected = rankingCase.MustBeTopK.Select(expectation => expectation.Token)
            .Concat(rankingCase.MustAppearBefore.SelectMany(pair => new[] { pair.Before, pair.After }));
        foreach (var token in expected)
        {
            if (!tokens.Contains(token))
                throw new InvalidDataException(
                    $"Case '{rankingCase.Id}' expects the unknown token '{token}'.");
        }
    }

    private static void RequireVersion(JsonElement root, int expected)
    {
        var actual = RequiredInt(root, "schemaVersion");
        if (actual != expected)
            throw new InvalidDataException($"Unsupported fixture schema {actual}; expected {expected}.");
    }

    private static JsonElement Required(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidDataException($"Missing required fixture property '{name}'.");

    private static string RequiredString(JsonElement parent, string name) =>
        Required(parent, name).GetString()
        ?? throw new InvalidDataException($"Fixture property '{name}' cannot be null.");

    private static string? OptionalString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static int RequiredInt(JsonElement parent, string name) => Required(parent, name).GetInt32();

    private static int OptionalInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) ? value.GetInt32() : 0;

    private static bool RequiredBool(JsonElement parent, string name) => Required(parent, name).GetBoolean();

    private static bool OptionalBool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.GetBoolean();

    private static bool? OptionalNullableBool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) ? value.GetBoolean() : null;

    private static double RequiredDouble(JsonElement parent, string name) => Required(parent, name).GetDouble();
}
