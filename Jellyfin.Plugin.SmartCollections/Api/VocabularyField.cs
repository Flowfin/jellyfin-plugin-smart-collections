using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Api;

/// <summary>
/// One field, as the vocabulary endpoint hands it to the page.
/// </summary>
/// <remarks>
/// Every column is the row's own, converted to strings and nothing else. A page that computed any
/// of them - which operators go with a type, say - would hold a second copy of a rule the engine
/// already decides, and the two would drift the first time one moved.
/// </remarks>
/// <param name="Name">The name a rule document writes.</param>
/// <param name="ValueType">The type the field holds.</param>
/// <param name="Operators">The operators it accepts, in the order a refusal lists them.</param>
/// <param name="Kinds">The item kinds it means anything for.</param>
/// <param name="ReachesTheLibrary">
/// The query property it narrows on, or <see langword="null"/> where it is read after the query.
/// </param>
/// <param name="Semantics">What the field holds, in one sentence.</param>
public sealed record VocabularyField(
    string Name,
    string ValueType,
    IReadOnlyList<string> Operators,
    IReadOnlyList<string> Kinds,
    string? ReachesTheLibrary,
    string Semantics);
