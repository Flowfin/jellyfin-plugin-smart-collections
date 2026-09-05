namespace Jellyfin.Plugin.SmartCollections.Evaluation;

/// <summary>
/// The five shapes a field's value takes when it is read off an item.
/// </summary>
/// <remarks>
/// This is not the field's declared type and the two are worth telling apart. A field's type is
/// what a document may write beside it, which <see cref="Rules.RuleFieldTable"/> declares; this is
/// what comes back off the item, which decides which comparisons mean anything. The pair that
/// separates them is <c>genres</c> and <c>name</c>: both declare a string, and one is a list of
/// them on the item while the other is one.
///
/// A member added here owes an arm in <see cref="ItemFieldReader"/> and an arm in
/// <see cref="ConditionMatcher"/>, and the suite refuses one that has neither.
/// </remarks>
public enum ItemFieldShape
{
    /// <summary>
    /// One string the library may leave unset.
    /// </summary>
    Text,

    /// <summary>
    /// Several strings, possibly none.
    /// </summary>
    TextList,

    /// <summary>
    /// One number the library may leave unset.
    /// </summary>
    Number,

    /// <summary>
    /// One instant the library may leave unset.
    /// </summary>
    Instant,

    /// <summary>
    /// One length of time the library may leave unset.
    /// </summary>
    Span
}
