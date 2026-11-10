namespace Lertaro.Core.SearchIndex;

/// <summary>The query failed; any results received before this failure are incomplete.</summary>
public sealed class SearchRegexTimeoutException : Exception
{
    public SearchRegexTimeoutException() : base("The regular expression exceeded its match time limit. The search was stopped.") { }

    public static bool IsTimeout(Exception error) => error is SearchRegexTimeoutException
        || (error is AggregateException aggregate && aggregate.InnerExceptions.Any(IsTimeout))
        || (error.InnerException is { } inner && IsTimeout(inner));
}
