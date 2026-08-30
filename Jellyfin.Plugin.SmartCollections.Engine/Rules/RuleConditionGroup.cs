using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// One group of a rule's composition, with the groups and the conditions it holds.
/// </summary>
/// <remarks>
/// Members are get-only and the two lists are handed out as read-only views, so a tree that
/// passed the composition stage is the tree every later stage sees. Nothing here can be
/// rearranged after the fact, which is what makes it worth saying that the tree preserves the
/// order the document wrote: a reader comparing two compiled rules is comparing two documents and
/// not two visit orders.
///
/// A condition is carried as its JSON Pointer rather than as its content. What a condition may
/// say is the field vocabulary's business, and this stage is the shape: which groups hold which
/// members, how deep the tree goes, and whether any group is empty. Carrying the pointer rather
/// than the parsed member is what keeps that boundary from moving - the stage that understands a
/// condition reads it from the same document, at the place this tree says it is, and this type
/// needs no opinion about it.
/// </remarks>
public sealed class RuleConditionGroup
{
    internal RuleConditionGroup(
        RuleConditionGroupKind kind,
        string pointer,
        IReadOnlyList<RuleConditionGroup> groups,
        IReadOnlyList<string> conditionPointers)
    {
        Kind = kind;
        Pointer = pointer;
        Groups = groups;
        ConditionPointers = conditionPointers;
    }

    /// <summary>
    /// Gets how this group combines what it holds.
    /// </summary>
    public RuleConditionGroupKind Kind { get; }

    /// <summary>
    /// Gets where this group is in the document, as a JSON Pointer.
    /// </summary>
    public string Pointer { get; }

    /// <summary>
    /// Gets the groups this group holds, in the order the document wrote them.
    /// </summary>
    public IReadOnlyList<RuleConditionGroup> Groups { get; }

    /// <summary>
    /// Gets where each condition this group holds is, in the order the document wrote them.
    /// </summary>
    public IReadOnlyList<string> ConditionPointers { get; }

    /// <summary>
    /// Gets how many members this group holds, groups and conditions together.
    /// </summary>
    /// <remarks>
    /// A group holding none is refused, so this is never zero on a tree that was accepted. It is
    /// here because the refusal is about this number and a reader checking that property should
    /// not have to add two counts to see it.
    /// </remarks>
    public int MemberCount => Groups.Count + ConditionPointers.Count;
}
