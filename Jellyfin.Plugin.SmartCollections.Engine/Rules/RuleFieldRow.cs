using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// One field, as the table declares it.
/// </summary>
/// <remarks>
/// Five things and no more: which field this is, what a document writes to name it, the type it
/// holds, the operators it accepts, and which item kinds it means anything for.
///
/// The written name is declared rather than derived from the member, for the reason
/// <see cref="RuleOperatorRow"/> gives about its own: deriving it would make the wire format a
/// property of a C# identifier, so renaming the member for a compiler warning would silently
/// break every rule document on every server.
///
/// HOW THE FIELD REACHES THE LIBRARY IS NO LONGER A COLUMN HERE, and this remark described one.
/// A sixth column named the property on the server's item query the field narrows on, or nothing
/// where the field was read off the item after the query returned, and the remark said that which
/// OPERATORS narrow inside the query was not that column's business. That was the defect rather
/// than an aside: a field can be answered by the server under one operator and not under another,
/// so a mark on the field alone is either too wide or too narrow for one of them. #31 decided it
/// on 2026-09-04 and the mark is a property of the field and operator PAIR, which
/// <see cref="RuleQueryTable"/> already declared one row at a time.
///
/// So nothing in this row answers whether the query carries a condition;
/// <c>RuleQueryTable.AnswersInTheQuery</c> does, and the answer takes both halves of the pair. A
/// row here says what a field IS - its name, its type, the operators it accepts and the kinds it
/// means anything for - and the pair table says what the server can do about each way of asking.
///
/// The query type is not named in this file, and that is deliberate. <c>docs/testing.md</c>
/// accounts for the files of this tree that COMPOSE a library query, and a check holds that page
/// by scanning the product sources for the type's name; this file composes nothing and would sit
/// in that population as a permanent false positive.
/// </remarks>
public sealed class RuleFieldRow
{
    internal RuleFieldRow(
        RuleField field,
        string name,
        RuleValueType valueType,
        IReadOnlyList<RuleOperator> operators,
        IReadOnlyList<RuleItemKind> kinds,
        string semantics)
    {
        Field = field;
        Name = name;
        ValueType = valueType;
        Operators = operators;
        Kinds = kinds;
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
    /// Gets what the field holds, in one sentence.
    /// </summary>
    public string Semantics { get; }

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
