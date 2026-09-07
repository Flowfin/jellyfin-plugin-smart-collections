using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Api;

/// <summary>
/// One field, as the vocabulary endpoint hands it to the page.
/// </summary>
/// <remarks>
/// Every column is the row's own, converted to strings and nothing else. A page that computed any
/// of them - which operators go with a type, say - would hold a second copy of a rule the engine
/// already decides, and the two would drift the first time one moved.
///
/// <para>
/// ONE OF THEM IS NOT THE FIELD ROW'S, AND SAYING SO IS THE POINT OF THIS PARAGRAPH.
/// <paramref name="AnsweredByTheQuery"/> comes from the compile table, because since #31 whether
/// the server's query carries a condition is a property of the field and operator PAIR rather than
/// of the field. It used to be one nullable string here, the query property the field narrowed on,
/// and that shape could not say that a field is answered by the server under one operator and not
/// under another. A page offering "this narrows in the query" against a field rather than against
/// the pair the operator was chosen for is telling an administrator something that is true of some
/// of their conditions.
/// </para>
/// </remarks>
/// <param name="Name">The name a rule document writes.</param>
/// <param name="ValueType">The type the field holds.</param>
/// <param name="Operators">The operators it accepts, in the order a refusal lists them.</param>
/// <param name="Kinds">The item kinds it means anything for.</param>
/// <param name="AnsweredByTheQuery">
/// The operators the server's own query answers this field under, in the order
/// <paramref name="Operators"/> lists them, and empty where every way of asking about it is
/// answered by the stage after the query.
/// </param>
/// <param name="Semantics">What the field holds, in one sentence.</param>
public sealed record VocabularyField(
    string Name,
    string ValueType,
    IReadOnlyList<string> Operators,
    IReadOnlyList<string> Kinds,
    IReadOnlyList<string> AnsweredByTheQuery,
    string Semantics);
