using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Evaluation;

/// <summary>
/// Runs one rule: compiles it, asks the server for what its query selects, applies what the query
/// could not carry, and answers with the ordered identifiers a refresh acts on.
/// </summary>
/// <remarks>
/// THIS IS THE ONLY PLACE A COMPILED QUERY IS ASKED, and it asks it through
/// <see cref="IRuleItemSource"/> rather than through the server's own library manager, so the step
/// runs against a list in the suite and needs no server. What it does with the answer is the part
/// worth reading carefully, because it is where a rule engine of this shape is usually wrong.
///
/// WHICH CONDITIONS REACH THE QUERY IS THE COMPOSITION TREE'S QUESTION AND IT IS ANSWERED HERE.
/// A server query is a conjunction, so a condition may be pushed into it only where it has to hold
/// for every item the rule collects, which is exactly a condition reachable from the root through
/// <see cref="RuleConditionGroupKind.All"/> groups and nothing else. A rule whose root is a
/// disjunction therefore pushes nothing and is bounded by its scope alone, which is slower and is
/// the only answer that is right: pushing one arm of an <c>anyOf</c> into the query would ask the
/// server for the items that arm selects and drop every item the other arm does.
/// <see cref="RuleQueryCompiler"/> says of itself that it is not handed the tree and that this
/// question is not its; this is where it is taken.
///
/// A CONDITION THE QUERY CARRIED IS NOT COMPARED AGAIN. The server compares its own cleaned form
/// of a name, a genre and a tag, so an item the query returned has already satisfied every pushed
/// condition, by the server's comparison. Re-running such a condition through
/// <see cref="ConditionMatcher"/> would apply this plugin's ordinal comparison to an item the
/// server matched some other way, and would drop items for a difference nobody wrote down. So a
/// pushed condition reads as satisfied, and what is compared here is what the compiler handed
/// back plus everything the tree kept out of the query.
///
/// THE ORDER IS THE IDENTIFIER AND ONLY THE IDENTIFIER, WHICH IS THE TIE-BREAK RATHER THAN THE
/// SORT. #39 declares that a rule may name a sort and that every sort ends with the identifier so
/// that the order is total; no rule document declares a sort yet, so what is left is the
/// tie-break, and a list ordered by it alone is total, reproducible and independent of the order
/// the server answered in. The sort this consumes arrives with that issue rather than here.
///
/// NOTHING HERE READS A CLOCK. The instant is an argument, it is handed to the compiler and to
/// every comparison that needs one, and it is carried out on the answer, which is what
/// <c>ambient-clock-in-the-engine</c> refuses the absence of.
/// </remarks>
public static class RuleEvaluator
{
    /// <summary>
    /// Runs a rule against the library the source answers for.
    /// </summary>
    /// <param name="document">The rule document, as validation accepted it.</param>
    /// <param name="items">The port the compiled query is asked through.</param>
    /// <param name="evaluatedAt">The instant the evaluation is given.</param>
    /// <returns>The ordered identifiers, or the reasons the rule was refused.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="document"/> or <paramref name="items"/> is <see langword="null"/>.
    /// </exception>
    public static RuleEvaluation Evaluate(
        RuleDocument document,
        IRuleItemSource items,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(items);

        using var parsed = JsonDocument.Parse(document.Text);
        var root = parsed.RootElement;

        var scope = RuleItemScopeReader.Read(root);
        if (!scope.IsAccepted)
        {
            return RuleEvaluation.Refused(scope.Errors, evaluatedAt);
        }

        var rule = ReadRule(root, scope.Kinds);
        if (rule.Errors.Count > 0)
        {
            return RuleEvaluation.Refused(rule.Errors, evaluatedAt);
        }

        var conjunctive = new HashSet<string>(StringComparer.Ordinal);
        CollectConjunctive(rule.Group!, conjunctive);

        var pushable = new List<RuleConditionValue>();
        foreach (var condition in rule.Conditions)
        {
            if (conjunctive.Contains(condition.Pointer))
            {
                pushable.Add(condition);
            }
        }

        var compilation = RuleQueryCompiler.Compile(scope.Kinds, pushable, evaluatedAt);
        if (!compilation.IsAccepted)
        {
            // Before the source is asked anything. A refused compilation carries an unnarrowed
            // query, so asking it would be asking the server for the whole scope on behalf of a
            // rule that does not compile.
            return RuleEvaluation.Refused(compilation.Errors, evaluatedAt);
        }

        var pushed = new HashSet<string>(conjunctive, StringComparer.Ordinal);
        foreach (var condition in compilation.AfterTheQuery)
        {
            pushed.Remove(condition.Pointer);
        }

        var byPointer = new Dictionary<string, RuleConditionValue>(StringComparer.Ordinal);
        foreach (var condition in rule.Conditions)
        {
            byPointer.Add(condition.Pointer, condition);
        }

        var collected = new List<Guid>();
        foreach (var item in items.Select(compilation.Query))
        {
            if (Satisfies(item, rule.Group!, byPointer, pushed, evaluatedAt))
            {
                collected.Add(item.Id);
            }
        }

        collected.Sort(static (left, right) => string.CompareOrdinal(Key(left), Key(right)));

        return RuleEvaluation.Accepted(collected, evaluatedAt);
    }

    /// <summary>
    /// The stages that read a rule out of a document validation has already accepted, walked in
    /// the order a document meets them.
    /// </summary>
    /// <param name="root">The document.</param>
    /// <param name="kinds">The kinds the rule collects.</param>
    /// <returns>The tree, the conditions, or the reasons a stage refused.</returns>
    /// <remarks>
    /// Read from the document rather than carried on <see cref="RuleDocument"/>, because that
    /// record deliberately carries the envelope and not the rule, and each stage is handed what
    /// the stage before it produced. A document that reached here has passed every one of these,
    /// so the refusals are unreachable through the store; they are answered rather than thrown
    /// because a caller may hand this a document it built itself, and a refusal is the answer this
    /// step already has a shape for.
    /// </remarks>
    private static RuleRead ReadRule(JsonElement root, IReadOnlyList<RuleItemKindRow> kinds)
    {
        if (!root.TryGetProperty("match", out var match))
        {
            return new RuleRead(
                null,
                [],
                [new RuleValidationError("/match", "The document declares no rule to evaluate.")]);
        }

        var composition = RuleCompositionReader.Read(match, "/match");
        if (!composition.IsAccepted)
        {
            return new RuleRead(null, [], composition.Errors);
        }

        var fields = RuleFieldReader.Read(root, composition.Group!, kinds);
        if (!fields.IsAccepted)
        {
            return new RuleRead(null, [], fields.Errors);
        }

        var operators = RuleOperatorReader.Read(root, fields.Fields);
        if (!operators.IsAccepted)
        {
            return new RuleRead(null, [], operators.Errors);
        }

        var values = RuleValueReader.Read(root, operators.Operators);

        return values.IsAccepted
            ? new RuleRead(composition.Group, values.Conditions, [])
            : new RuleRead(null, [], values.Errors);
    }

    /// <summary>
    /// Collects the conditions that have to hold for every item the rule collects.
    /// </summary>
    /// <param name="group">The group to walk.</param>
    /// <param name="pointers">The pointers collected so far.</param>
    /// <remarks>
    /// The walk stops at the first group that is not a conjunction, which is what makes the answer
    /// right rather than merely conservative: a condition under an <c>anyOf</c> or a <c>none</c>
    /// does not have to hold, so pushing it into the query would narrow the server's answer past
    /// what the rule says.
    /// </remarks>
    private static void CollectConjunctive(RuleConditionGroup group, HashSet<string> pointers)
    {
        if (group.Kind != RuleConditionGroupKind.All)
        {
            return;
        }

        foreach (var pointer in group.ConditionPointers)
        {
            pointers.Add(pointer);
        }

        foreach (var inner in group.Groups)
        {
            CollectConjunctive(inner, pointers);
        }
    }

    /// <summary>
    /// Answers whether an item satisfies a group of the rule's composition.
    /// </summary>
    /// <param name="item">The item the query returned.</param>
    /// <param name="group">The group.</param>
    /// <param name="byPointer">Every condition of the rule, by its pointer.</param>
    /// <param name="pushed">The pointers of the conditions the query already answered.</param>
    /// <param name="evaluatedAt">The instant the evaluation was given.</param>
    /// <returns><see langword="true"/> where the item satisfies the group.</returns>
    /// <remarks>
    /// Written as one walk over the members and one answer per kind rather than as three walks.
    /// The members are visited in the order the document wrote them, conditions before groups,
    /// which decides nothing about the answer and everything about which comparison runs first on
    /// an item that is going to be rejected anyway.
    ///
    /// THE LAST ARM IS THE NEGATION RATHER THAN A THROW, and that is the choice between an arm
    /// that ships unproven and one that cannot. A kind with no arm here is unreachable through a
    /// document: <see cref="RuleCompositionReader"/> declares the name each kind is written under
    /// and refuses any other member, so a fourth kind reaching this would first have to be given a
    /// name there. An arm throwing for it could therefore never be executed by a test, and a guard
    /// no test can reach is refused in this repository. What holds the case instead is the
    /// composition vocabulary's own rule, which is that a kind added there owes a name in the
    /// reader and a section in <c>docs/rule-composition.md</c>.
    /// </remarks>
    private static bool Satisfies(
        BaseItem item,
        RuleConditionGroup group,
        Dictionary<string, RuleConditionValue> byPointer,
        HashSet<string> pushed,
        DateTimeOffset evaluatedAt)
    {
        var held = 0;
        var members = 0;

        foreach (var pointer in group.ConditionPointers)
        {
            members++;
            if (Holds(item, pointer, byPointer, pushed, evaluatedAt))
            {
                held++;
            }
        }

        foreach (var inner in group.Groups)
        {
            members++;
            if (Satisfies(item, inner, byPointer, pushed, evaluatedAt))
            {
                held++;
            }
        }

        return group.Kind switch
        {
            RuleConditionGroupKind.All => held == members,
            RuleConditionGroupKind.Any => held > 0,
            _ => held == 0
        };
    }

    /// <summary>
    /// Answers whether an item satisfies one condition of the rule.
    /// </summary>
    /// <param name="item">The item the query returned.</param>
    /// <param name="pointer">Where the condition is in the document.</param>
    /// <param name="byPointer">Every condition of the rule, by its pointer.</param>
    /// <param name="pushed">The pointers of the conditions the query already answered.</param>
    /// <param name="evaluatedAt">The instant the evaluation was given.</param>
    /// <returns><see langword="true"/> where the item satisfies it.</returns>
    private static bool Holds(
        BaseItem item,
        string pointer,
        Dictionary<string, RuleConditionValue> byPointer,
        HashSet<string> pushed,
        DateTimeOffset evaluatedAt)
        => pushed.Contains(pointer)
           || ConditionMatcher.Matches(item, byPointer[pointer], evaluatedAt);

    /// <summary>
    /// The sort key of an identifier.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The key.</returns>
    /// <remarks>
    /// The text form rather than the value, because the comparison a reader can reproduce is the
    /// one over the string an expected file would hold. Every identifier renders to the same
    /// thirty-two characters from the same alphabet, so an ordinal comparison over them is total
    /// and is the same on every platform.
    /// </remarks>
    private static string Key(Guid id) => id.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// What reading a rule out of a document produced.
    /// </summary>
    /// <param name="Group">The composition tree, or <see langword="null"/> where a stage refused.</param>
    /// <param name="Conditions">The conditions, with their values parsed.</param>
    /// <param name="Errors">The reasons a stage refused, empty where none did.</param>
    private sealed record RuleRead(
        RuleConditionGroup? Group,
        IReadOnlyList<RuleConditionValue> Conditions,
        IReadOnlyList<RuleValidationError> Errors);
}
