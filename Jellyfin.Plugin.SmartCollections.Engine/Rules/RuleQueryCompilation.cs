using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// What compiling a rule's conditions produced: the query to ask the server, the conditions the
/// query does not answer, or the reasons the conditions could not be compiled at all.
/// </summary>
/// <remarks>
/// The two accepted halves are both part of the answer and neither stands alone. A caller that
/// asked the query and ignored <see cref="AfterTheQuery"/> would return items the rule does not
/// match, which is the failure this type exists to make impossible to write by accident: the
/// conditions the query could not carry are handed back rather than dropped.
/// </remarks>
public sealed class RuleQueryCompilation
{
    private RuleQueryCompilation(
        InternalItemsQuery query,
        IReadOnlyList<RuleConditionValue> afterTheQuery,
        IReadOnlyList<RuleValidationError> errors)
    {
        Query = query;
        AfterTheQuery = afterTheQuery;
        Errors = errors;
    }

    /// <summary>
    /// Gets the query the compiled conditions narrow.
    /// </summary>
    /// <remarks>
    /// Every property this compilation did not write is left where the server's own constructor
    /// leaves it. A refused compilation carries an unnarrowed query rather than a partly narrowed
    /// one, because a query built from some of a rule's conditions selects a superset of what the
    /// rule means and looks like an answer.
    /// </remarks>
    public InternalItemsQuery Query { get; }

    /// <summary>
    /// Gets the conditions the query does not answer, in the order the document wrote them.
    /// </summary>
    public IReadOnlyList<RuleConditionValue> AfterTheQuery { get; }

    /// <summary>
    /// Gets the reasons the conditions were refused, empty where they were compiled.
    /// </summary>
    public IReadOnlyList<RuleValidationError> Errors { get; }

    /// <summary>
    /// Gets a value indicating whether the conditions compiled.
    /// </summary>
    public bool IsAccepted => Errors.Count == 0;

    /// <summary>
    /// A compilation that produced a query.
    /// </summary>
    /// <param name="query">The narrowed query.</param>
    /// <param name="afterTheQuery">The conditions the query does not answer.</param>
    /// <returns>The compilation.</returns>
    public static RuleQueryCompilation Accepted(
        InternalItemsQuery query,
        IReadOnlyList<RuleConditionValue> afterTheQuery)
        => new(query, afterTheQuery, []);

    /// <summary>
    /// A compilation that was refused.
    /// </summary>
    /// <param name="errors">The reasons, in the order they were found.</param>
    /// <returns>The compilation.</returns>
    public static RuleQueryCompilation Refused(IReadOnlyList<RuleValidationError> errors)
        => new(new InternalItemsQuery(), [], errors);
}
