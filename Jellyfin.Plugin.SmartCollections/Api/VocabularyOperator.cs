using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Api;

/// <summary>
/// One operator, as the vocabulary endpoint hands it to the page.
/// </summary>
/// <remarks>
/// The two type lists are separate because they are separate questions: one is the set of field
/// types the operator applies to and the other is the set of types the value beside it may be
/// written as, and for one operator in the set they differ. A page carrying one list would offer
/// the wrong control for that operator.
/// </remarks>
/// <param name="Name">The name a rule document writes.</param>
/// <param name="FieldTypes">The field types this operator applies to.</param>
/// <param name="ValueTypes">The types the value beside it may be written as, empty where it takes none.</param>
/// <param name="TakesAValue">Whether a condition writes a value beside it at all.</param>
/// <param name="TakesAList">Whether the value beside it is written as a list.</param>
/// <param name="Semantics">What the comparison asks, in one sentence.</param>
public sealed record VocabularyOperator(
    string Name,
    IReadOnlyList<string> FieldTypes,
    IReadOnlyList<string> ValueTypes,
    bool TakesAValue,
    bool TakesAList,
    string Semantics);
