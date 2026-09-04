using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
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
///
/// IT IS ALSO HANDED THE SCOPE, AND THAT ONE IS NOT OPTIONAL. Every query this stage produces is
/// bounded by the item kinds the rule declares it collects, whether or not a single condition
/// compiled, so a rule made entirely of conditions the query cannot express still asks the server
/// for films and series rather than for the library. A compiler that could return an unbounded
/// query would put that bound in the caller's keeping, which is where a bound is forgotten once.
///
/// AND IT IS HANDED THE INSTANT THE EVALUATION WAS GIVEN, once, as an argument. A rule saying
/// "released in the last thirty days" reads a clock somewhere, and where it reads one decides
/// whether the rule answers the same way twice. Here the instant is an input: the caller resolves
/// it once per evaluation and passes that one value in, every pair that needs it is handed the
/// same one, and the engine itself reads no clock, which <c>ambient-clock-in-the-engine</c>
/// refuses rather than trusts. Two relative conditions in one rule therefore see one instant,
/// because there is one parameter for them to see, and a rule compiled twice at one instant is
/// one query.
/// </remarks>
public static class RuleQueryCompiler
{
    /// <summary>
    /// Compiles the kinds a rule collects and the conditions that all have to hold into one query.
    /// </summary>
    /// <param name="scope">The kinds the rule collects, as the scope stage read them.</param>
    /// <param name="conditions">The conditions, with their values already parsed.</param>
    /// <param name="evaluatedAt">
    /// The instant the evaluation was given. Every span a condition declares ends here, and the
    /// value is recorded with the evaluation's result by whatever runs one, so a report about a
    /// collection can be reproduced at the instant it was made rather than at the instant it is
    /// read.
    /// </param>
    /// <returns>The query, the conditions it does not answer, or the reasons it was refused.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="scope"/> or <paramref name="conditions"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is empty.</exception>
    public static RuleQueryCompilation Compile(
        IReadOnlyList<RuleItemKindRow> scope,
        IReadOnlyList<RuleConditionValue> conditions,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(conditions);

        // An empty scope is refused here rather than compiled into an empty include list, because
        // the server reads an empty one as no narrowing at all. The scope stage cannot produce
        // one; this arm is for a caller that built a scope some other way, and it throws rather
        // than returning a refusal because it is a fault in the caller and not in a document.
        if (scope.Count == 0)
        {
            throw new ArgumentException(
                "A rule collects at least one item kind, and an empty scope would compile to a query that narrows on none.",
                nameof(scope));
        }

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

            // Every property the row would write is checked before any is written, so a row
            // writing two never claims one and is refused on the other.
            var taken = FirstTaken(row, written);
            if (taken is not null)
            {
                errors.Add(Refuse(condition, row, taken, written[taken]));
                continue;
            }

            if (!row.TryWrite(query, condition.Values, evaluatedAt))
            {
                afterTheQuery.Add(condition);
                continue;
            }

            foreach (var property in row.QueryProperties)
            {
                written.Add(property, condition.Pointer);
            }
        }

        if (errors.Count > 0)
        {
            return RuleQueryCompilation.Refused(errors);
        }

        // Written last so that a refused compilation carries a query nobody can mistake for an
        // answer, which is the property RuleQueryCompilation declares about itself. The list is
        // built from the scope in the order the table declares, so ExcludeItemTypes is left where
        // the server's own constructor leaves it: this plugin selects by naming what it collects
        // and never by naming what it does not.
        var kinds = new BaseItemKind[scope.Count];
        for (var index = 0; index < scope.Count; index++)
        {
            kinds[index] = scope[index].ServerKind;
        }

        query.IncludeItemTypes = kinds;

        return RuleQueryCompilation.Accepted(query, afterTheQuery);
    }

    /// <summary>
    /// Finds the first property a row would write that another condition already wrote.
    /// </summary>
    /// <param name="row">The row about to write.</param>
    /// <param name="written">The properties written so far, keyed on their name.</param>
    /// <returns>The property, or <see langword="null"/> where every one the row names is free.</returns>
    private static string? FirstTaken(RuleQueryRow row, Dictionary<string, string> written)
    {
        foreach (var property in row.QueryProperties)
        {
            if (written.ContainsKey(property))
            {
                return property;
            }
        }

        return null;
    }

    /// <summary>
    /// Refuses a second condition that would write a property another condition already wrote.
    /// </summary>
    /// <param name="condition">The second condition.</param>
    /// <param name="row">The row it compiles through.</param>
    /// <param name="property">The property both conditions write.</param>
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
    /// <c>notContains</c> write two different properties and are both compiled, and
    /// <c>premiereDate withinLast</c> beside <c>premiereDate before</c> is refused on the ceiling
    /// they share rather than on the field.
    /// </remarks>
    private static RuleValidationError Refuse(RuleConditionValue condition, RuleQueryRow row, string property, string first)
    {
        var field = RuleFieldTable.Of(row.Field).Name;

        return new RuleValidationError(
            condition.Pointer,
            "The condition at \"" + first + "\" already narrows the query on \"" + field
            + "\". Both conditions write " + property
            + ", and the query holds one value for it.");
    }
}
