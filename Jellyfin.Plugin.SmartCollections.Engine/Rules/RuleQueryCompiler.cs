using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// Turns the conditions a rule wrote into a query the server's own item store answers.
/// </summary>
/// <remarks>
/// The route this plugin takes is to ask the server for a narrowed set rather than to ask it for
/// everything and filter item by item. The prior art in this space takes the second, projects each
/// item onto a class, compiles a predicate per condition and manages the cost of the expensive
/// fields with hand-written caches. That design's cost is paid on every item on every run; this
/// one's is paid on the pairs the query cannot express, and this table is where that boundary is
/// declared rather than discovered.
///
/// The stage is handed what the value stage produced, which is a flat list of conditions with
/// their field row, their operator row and their parsed values. It is NOT handed the composition
/// tree, and that is a boundary rather than an omission: a server query is a conjunction, so only
/// conditions that all have to hold can be pushed into it, and which of a rule's conditions those
/// are is the tree's question rather than this one's.
/// </remarks>
public static class RuleQueryCompiler
{
    /// <summary>
    /// Compiles conditions that all have to hold into one query.
    /// </summary>
    /// <param name="conditions">The conditions, with their values already parsed.</param>
    /// <returns>The query, the conditions it does not answer, or the reasons it was refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="conditions"/> is <see langword="null"/>.</exception>
    public static RuleQueryCompilation Compile(IReadOnlyList<RuleConditionValue> conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);

        var query = new InternalItemsQuery();
        var afterTheQuery = new List<RuleConditionValue>();
        var errors = new List<RuleValidationError>();
        var written = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var condition in conditions)
        {
            var row = RuleQueryTable.Find(condition.Field.Field, condition.Operator.Operator);
            if (row is null)
            {
                afterTheQuery.Add(condition);
                continue;
            }

            if (written.TryGetValue(row.QueryProperty, out var first))
            {
                errors.Add(Refuse(condition, row, first));
                continue;
            }

            if (!row.TryWrite(query, condition.Values))
            {
                afterTheQuery.Add(condition);
                continue;
            }

            written.Add(row.QueryProperty, condition.Pointer);
        }

        return errors.Count > 0
            ? RuleQueryCompilation.Refused(errors)
            : RuleQueryCompilation.Accepted(query, afterTheQuery);
    }

    /// <summary>
    /// Refuses a second condition that would write a property another condition already wrote.
    /// </summary>
    /// <param name="condition">The second condition.</param>
    /// <param name="row">The row it compiles through.</param>
    /// <param name="first">Where the condition that already wrote the property sits.</param>
    /// <returns>The refusal.</returns>
    /// <remarks>
    /// Refused rather than combined, and the choice is between two defensible readings rather than
    /// between a right one and a wrong one. Combining would have to invent a meaning - two years
    /// written with <c>equals</c> could be read as a rule matching neither, since both have to
    /// hold, or as the list the query's own year array would make of them - and neither reading is
    /// what the document says. What is not defensible is the third option, which is what a plain
    /// assignment does: the second write replaces the first, the query asks half the rule, and
    /// nothing says so.
    ///
    /// The refusal is per PROPERTY rather than per field, because the property is where the
    /// replacement would happen. Two conditions on <c>tags</c> written as <c>contains</c> and
    /// <c>notContains</c> write two different properties and are both compiled.
    /// </remarks>
    private static RuleValidationError Refuse(RuleConditionValue condition, RuleQueryRow row, string first)
    {
        var field = RuleFieldTable.Of(row.Field).Name;

        return new RuleValidationError(
            condition.Pointer,
            "The condition at \"" + first + "\" already narrows the query on \"" + field
            + "\". Both conditions write " + row.QueryProperty
            + ", and the query holds one value for it.");
    }
}
