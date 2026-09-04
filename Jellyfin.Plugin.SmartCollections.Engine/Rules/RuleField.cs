namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// The fields a rule may name.
/// </summary>
/// <remarks>
/// The vocabulary is declared here rather than reflected over a projection class. Both existing
/// plugins in this space look a document's field string up as a property with
/// <c>Expression.PropertyOrField</c>, so the legal set is whatever properties happen to sit on
/// that class: nobody wrote it down, nothing can list it back to the person writing a rule, and a
/// typo arrives at evaluation as an exception rather than at validation as a message.
///
/// A member added here owes a row in <see cref="RuleFieldTable"/> and a section in
/// <c>docs/rule-fields.md</c>, and the suite refuses one that has neither. That is what makes the
/// vocabulary closed in practice rather than in a sentence.
///
/// Which item kinds each field applies to is a column the table carries, landed under #69. Every
/// field declared here applies to both kinds the first version collects, so the column narrows
/// nothing today and the refusal that reads it fires on no document anybody can write. That is a
/// fact about this vocabulary rather than about the guard, and it is where a field that means
/// nothing for one kind would be declared rather than inferred.
/// </remarks>
public enum RuleField
{
    /// <summary>
    /// The rating the community gives the item, out of ten.
    /// </summary>
    CommunityRating,

    /// <summary>
    /// When the server first saw the item.
    /// </summary>
    DateAdded,

    /// <summary>
    /// The genres the item carries.
    /// </summary>
    Genres,

    /// <summary>
    /// The item's title, as the library holds it.
    /// </summary>
    Name,

    /// <summary>
    /// The age classification the item carries.
    /// </summary>
    OfficialRating,

    /// <summary>
    /// The item's description.
    /// </summary>
    Overview,

    /// <summary>
    /// When the item was first released.
    /// </summary>
    PremiereDate,

    /// <summary>
    /// The year the item was produced.
    /// </summary>
    ProductionYear,

    /// <summary>
    /// How long the item runs for.
    /// </summary>
    Runtime,

    /// <summary>
    /// The tags the item carries.
    /// </summary>
    Tags
}
