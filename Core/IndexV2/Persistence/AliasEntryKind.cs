namespace Lertaro.Core.IndexV2.Persistence;

/// <summary>
/// What one baked alias entry's payload is. The snapshot has always stored provider-tagged
/// variable-length payloads, and from format version 11 a reading grid could be baked alongside the
/// alias strings -- one decoded as a UTF-8 alias string, the other as a name's whole reading space.
/// Nothing writes <see cref="Grid"/> any more (a provider's readings are decided live), but a snapshot
/// on disk can still hold entries of that kind, so the column and its values stay.
/// </summary>
/// <remarks>
/// A separate column rather than a bit smuggled into the provider id: provider ids are handed out by
/// <c>AliasProviderRegistry</c> and their width is that registry's business, while the payload shape is
/// this format's.
/// </remarks>
internal enum AliasEntryKind : byte
{
    String = 0,
    Grid = 1,
}
