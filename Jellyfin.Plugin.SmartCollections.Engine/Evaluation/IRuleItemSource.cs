using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Evaluation;

/// <summary>
/// The one question an evaluation asks the server: which items a compiled query selects.
/// </summary>
/// <remarks>
/// A port rather than the server's own library manager, for the reason
/// <see cref="Membership.ICollectionMembershipWriter"/> is one: the type the server hands out has
/// eighty-four members and holding a real one means a running server, which is what makes an
/// evaluation untestable. One method over a query and a list of items is the whole surface this
/// step needs, and a fake behind it is a list.
///
/// THE QUERY IS AN ARGUMENT AND IS NEVER BUILT HERE. What narrows a query is
/// <see cref="Rules.RuleQueryCompiler"/> and nothing else, so an implementation of this interface
/// that added a property of its own would be narrowing a rule somewhere no rule can be read. An
/// implementation forwards the query it is handed and answers with what came back.
///
/// The answer is a list rather than a stream. What the caller does with it is order it and reduce
/// it to identifiers, both of which need the whole set, and a stream would buy a laziness the next
/// line spends anyway.
/// </remarks>
public interface IRuleItemSource
{
    /// <summary>
    /// Answers a compiled query with the items it selects.
    /// </summary>
    /// <param name="query">The query, as the compiler narrowed it.</param>
    /// <returns>The items, in whatever order the server answered with.</returns>
    /// <remarks>
    /// THE ORDER OF THE ANSWER IS NOT PART OF THE CONTRACT. An implementation may answer in any
    /// order and the same implementation may answer in two orders on two calls; what an evaluation
    /// produces is ordered by the step that reads this, so a rule cannot come to depend on the
    /// order a repository happened to give it.
    /// </remarks>
    IReadOnlyList<BaseItem> Select(InternalItemsQuery query);
}
