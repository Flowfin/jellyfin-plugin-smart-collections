namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// One term of a rule's declared sort: a field, and the direction it is read in.
/// </summary>
/// <remarks>
/// A term carries the field's ROW rather than its name, so everything downstream of the reader
/// works from what the table declares rather than from a string it would have to look up again.
/// </remarks>
/// <param name="Field">The field this term orders by.</param>
/// <param name="Direction">The direction it orders in.</param>
/// <param name="Pointer">Where the term sits in the document, for a refusal to point at.</param>
public sealed record RuleSortTerm(RuleFieldRow Field, RuleSortDirection Direction, string Pointer);
