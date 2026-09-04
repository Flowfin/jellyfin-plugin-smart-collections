using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// One field, as the table declares it.
/// </summary>
/// <remarks>
/// Six things and no more: which field this is, what a document writes to name it, the type it
/// holds, the operators it accepts, which item kinds it means anything for, and how it reaches
/// the library.
///
/// The written name is declared rather than derived from the member, for the reason
/// <see cref="RuleOperatorRow"/> gives about its own: deriving it would make the wire format a
/// property of a C# identifier, so renaming the member for a compiler warning would silently
/// break every rule document on every server.
///
/// <see cref="QueryProperty"/> is the name of the property on the server's item query that the
/// field reaches the library through, or <see langword="null"/> where the field is read off the
/// item after the query has returned. Those are the only two ways a field can reach the library,
/// and a row says which one it is rather than leaving a reader to infer it from the compiler.
/// WHICH OPERATORS NARROW INSIDE THE QUERY AND WHICH NARROW AFTER IT IS NOT THIS COLUMN. A row
/// names the property the field is about; how a particular operator over that field is compiled is
/// the compiler's business, and the post-query stage it may fall back to is declared separately.
///
/// The query type is named in <c>docs/rule-fields.md</c> and in the suite rather than in this
/// file, and that is deliberate. <c>docs/testing.md</c> accounts for the files of this tree that
/// COMPOSE a library query, and a check holds that page by scanning the product sources for the
/// type's name; this file composes nothing and would sit in that population as a permanent false
/// positive. The name it declines to write is one string in the reflection the suite runs against
/// the real type, which is a stronger reading of the column than a mention here would be.
/// </remarks>
public sealed class RuleFieldRow
{
    internal RuleFieldRow(
        RuleField field,
        string name,
        RuleValueType valueType,
        IReadOnlyList<RuleOperator> operators,
        IReadOnlyList<RuleItemKind> kinds,
        string? queryProperty,
        string semantics)
    {
        Field = field;
        Name = name;
        ValueType = valueType;
        Operators = operators;
        Kinds = kinds;
        QueryProperty = queryProperty;
        Semantics = semantics;
    }

    /// <summary>
    /// Gets the field this row declares.
    /// </summary>
    public RuleField Field { get; }

    /// <summary>
    /// Gets the name a rule document writes to name it.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the type this field holds.
    /// </summary>
    /// <remarks>
    /// The FIELD's type, which is not always the type of the value written beside it. For every
    /// operator but <c>withinLast</c> the two are the same type, so this column read as either end
    /// gave the same answer; the operator table is where the two ends are told apart, and this
    /// column is the field end.
    /// </remarks>
    public RuleValueType ValueType { get; }

    /// <summary>
    /// Gets the operators this field accepts, in the order a refusal lists them.
    /// </summary>
    /// <remarks>
    /// A subset of what the operator table says the field's type allows, never a superset. The
    /// operator table answers whether an operator can compare a value of a type at all; this
    /// column answers whether the comparison means anything for this particular field, which is
    /// the narrower question and the one an operator writing a rule is asking.
    /// </remarks>
    public IReadOnlyList<RuleOperator> Operators { get; }

    /// <summary>
    /// Gets the item kinds this field means anything for, in the order the kind table declares
    /// them.
    /// </summary>
    /// <remarks>
    /// A FIELD IS NARROWED TO THE KINDS IT MEANS, decided on #69 on 2026-09-04. The alternative
    /// was to widen the kinds a rule may collect until some field means nothing for one of them,
    /// and it was refused because it changes what a rule collects in order to give a refusal
    /// something to bite on.
    ///
    /// Every field declared today names both kinds, so no document anybody can write reaches the
    /// refusal that reads this column. That is a fact about the vocabulary rather than about the
    /// guard, and it is why the proof that the guard bites is a row the suite builds rather than a
    /// document: <c>RuleFieldScopeTests</c> is where that is stated beside the test rather than
    /// left for a reader to notice.
    ///
    /// The column is on the FIELD rather than on the field and operator pair, which is the
    /// narrower question it answers: whether the thing the field is about exists for an item of
    /// that kind at all. Whether a particular comparison over it can be pushed into the query is
    /// a different column with a different subject.
    /// </remarks>
    public IReadOnlyList<RuleItemKind> Kinds { get; }

    /// <summary>
    /// Gets the property on the server's item query that the field reaches the library through,
    /// or <see langword="null"/> where the field is read after the query has returned.
    /// </summary>
    public string? QueryProperty { get; }

    /// <summary>
    /// Gets what the field holds, in one sentence.
    /// </summary>
    public string Semantics { get; }

    /// <summary>
    /// Gets a value indicating whether the field is read after the query rather than narrowed by
    /// it.
    /// </summary>
    public bool IsPostQuery => QueryProperty is null;

    /// <summary>
    /// Whether this field means anything for an item of the given kind.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns><see langword="true"/> where the field applies to it.</returns>
    public bool AppliesTo(RuleItemKind kind)
    {
        foreach (var declared in Kinds)
        {
            if (declared == kind)
            {
                return true;
            }
        }

        return false;
    }
}
