namespace Lertaro.Core.IndexV2.Persistence;

/// <summary>
/// What one baked alias entry's payload is. The snapshot has always stored provider-tagged
/// variable-length payloads; the reading grid adds a second payload shape, and the two are not
/// interchangeable -- one decodes as a UTF-8 alias string, the other is <c>AliasGridFormat</c>'s
/// encoding of a name's whole reading space.
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
