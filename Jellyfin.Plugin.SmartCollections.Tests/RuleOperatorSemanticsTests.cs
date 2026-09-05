using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Evaluation;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// Every operator the table declares, asserted against an item that satisfies it and an item that
/// does not.
/// </summary>
/// <remarks>
/// The clause this file exists for asks that every operator carry at least one test asserting the
/// semantics its row states. Until the post-query stage landed there was nothing in this tree that
/// could evaluate a condition over an item at all, so the sentence a row states could only be read;
/// now it can be executed.
///
/// WHAT IS ASSERTED IS THIS PLUGIN'S OWN COMPARISON AND NOT THE SERVER'S, and the distinction is
/// the one the operator table already draws about itself. Seven of the seventeen operators are
/// pairs the compiler pushes into the server's item query for the fields that carry a query
/// property, and there the comparison performed is the server's cleaned one, which no test in this
/// suite reaches. Every one of the seventeen also reaches the post-query stage - under a
/// disjunction, under a negation, or over a field with no query property - and that is what these
/// cases execute. So the clause is met for the meaning this plugin implements, and the server's
/// translation of the seven stays where the compiler's own table records it.
///
/// The cases are written out rather than derived. What a comparison MEANS is the subject, so a
/// case generated from the same table the comparison is written against would agree with it by
/// construction. What IS derived is the completeness: the sweep at the end reads the declared
/// operator set and refuses a case list that does not cover it exactly, so an operator added to the
/// vocabulary tomorrow reds this file rather than passing it silently.
/// </remarks>
public class RuleOperatorSemanticsTests
{
    private static readonly DateTimeOffset Given = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// One operator, a field its row accepts, the values a document writes beside it, and the two
    /// items that separate its answers.
    /// </summary>
    /// <param name="Operator">The operator under test.</param>
    /// <param name="Field">The field, as a document writes it.</param>
    /// <param name="Values">The values, as a document writes them.</param>
    /// <param name="Satisfying">An item the condition collects.</param>
    /// <param name="Failing">An item it does not.</param>
    private sealed record Case(
        RuleOperator Operator,
        string Field,
        string[] Values,
        BaseItem Satisfying,
        BaseItem Failing);

    private static IReadOnlyList<Case> Cases() =>
    [
        new(
            RuleOperator.Equals,
            "officialRating",
            ["PG-13"],
            new Movie { OfficialRating = "PG-13" },
            new Movie { OfficialRating = "R" }),
        new(
            RuleOperator.NotEquals,
            "officialRating",
            ["R"],
            new Movie { OfficialRating = "PG-13" },
            new Movie { OfficialRating = "R" }),
        new(
            RuleOperator.Contains,
            "overview",
            ["heist"],
            new Movie { Overview = "A heist in three acts." },
            new Movie { Overview = "A quiet film about bread." }),
        new(
            RuleOperator.NotContains,
            "overview",
            ["heist"],
            new Movie { Overview = "A quiet film about bread." },
            new Movie { Overview = "A heist in three acts." }),
        new(
            RuleOperator.StartsWith,
            "overview",
            ["A quiet"],
            new Movie { Overview = "A quiet film about bread." },
            new Movie { Overview = "Not a quiet film at all." }),
        new(
            RuleOperator.EndsWith,
            "overview",
            ["bread."],
            new Movie { Overview = "A quiet film about bread." },
            new Movie { Overview = "A quiet film about cake." }),
        new(
            RuleOperator.In,
            "officialRating",
            ["R", "PG-13"],
            new Movie { OfficialRating = "PG-13" },
            new Movie { OfficialRating = "PG" }),
        new(
            RuleOperator.NotIn,
            "officialRating",
            ["R", "PG"],
            new Movie { OfficialRating = "PG-13" },
            new Movie { OfficialRating = "R" }),
        new(
            RuleOperator.GreaterThan,
            "communityRating",
            ["7"],
            new Movie { CommunityRating = 8f },
            new Movie { CommunityRating = 7f }),
        new(
            RuleOperator.GreaterThanOrEqual,
            "communityRating",
            ["8"],
            new Movie { CommunityRating = 8f },
            new Movie { CommunityRating = 7.9f }),
        new(
            RuleOperator.LessThan,
            "communityRating",
            ["8"],
            new Movie { CommunityRating = 7.9f },
            new Movie { CommunityRating = 8f }),
        new(
            RuleOperator.LessThanOrEqual,
            "communityRating",
            ["8"],
            new Movie { CommunityRating = 8f },
            new Movie { CommunityRating = 8.1f }),
        new(
            RuleOperator.IsEmpty,
            "officialRating",
            [],
            new Movie(),
            new Movie { OfficialRating = "PG-13" }),
        new(
            RuleOperator.IsNotEmpty,
            "officialRating",
            [],
            new Movie { OfficialRating = "PG-13" },
            new Movie()),
        new(
            RuleOperator.Before,
            "premiereDate",
            ["1995-01-01T00:00:00Z"],
            new Movie { PremiereDate = new DateTime(1994, 6, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Movie { PremiereDate = new DateTime(1995, 1, 1, 0, 0, 0, DateTimeKind.Utc) }),
        new(
            RuleOperator.After,
            "premiereDate",
            ["1993-01-01T00:00:00Z"],
            new Movie { PremiereDate = new DateTime(1994, 6, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Movie { PremiereDate = new DateTime(1993, 1, 1, 0, 0, 0, DateTimeKind.Utc) }),
        new(
            RuleOperator.WithinLast,
            "dateAdded",
            ["P30D"],
            new Movie { DateCreated = new DateTime(2025, 12, 20, 0, 0, 0, DateTimeKind.Utc) },
            new Movie { DateCreated = new DateTime(2025, 11, 20, 0, 0, 0, DateTimeKind.Utc) })
    ];

    public static TheoryData<RuleOperator> Operators()
    {
        var data = new TheoryData<RuleOperator>();

        foreach (var row in Cases())
        {
            data.Add(row.Operator);
        }

        return data;
    }

    /// <summary>
    /// The clause this file carries: each operator, executed against an item its sentence collects
    /// and an item its sentence does not.
    /// </summary>
    /// <param name="operator">The operator under test.</param>
    /// <remarks>
    /// Both directions, because an operator asserted only on the item it collects is satisfied by
    /// a comparison that answers true for everything, and half the seventeen have a neighbour that
    /// would.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Operators))]
    public void EachOperatorCollectsTheItemItsSentenceDescribesAndNoOther(RuleOperator @operator)
    {
        var row = Cases().Single(entry => entry.Operator == @operator);
        var condition = RuleConditionFixture.Condition(row.Field, RuleOperatorTable.Of(@operator).Name, row.Values);

        Assert.True(
            ConditionMatcher.Matches(row.Satisfying, condition, Given),
            RuleOperatorTable.Of(@operator).Semantics + " The satisfying item was not collected.");
        Assert.False(
            ConditionMatcher.Matches(row.Failing, condition, Given),
            RuleOperatorTable.Of(@operator).Semantics + " The failing item was collected.");
    }

    /// <summary>
    /// The completeness leg, derived rather than counted by hand. Without it an operator added to
    /// the vocabulary passes this file by not being in it, which is the only day this file matters.
    /// </summary>
    [Fact]
    public void EveryOperatorTheTableDeclaresHasACaseHereAndNoCaseNamesOneItDoesNot()
    {
        var declared = RuleOperatorTable.Rows
            .Select(row => row.Operator)
            .OrderBy(@operator => @operator)
            .ToArray();
        var covered = Cases()
            .Select(row => row.Operator)
            .OrderBy(@operator => @operator)
            .ToArray();

        Assert.Equal(declared, covered);
        Assert.Equal(covered.Length, covered.Distinct().Count());
    }

    /// <summary>
    /// Every case names a field whose own row declares the operator it is testing, so a case cannot
    /// be asserting a pair no document can write.
    /// </summary>
    [Fact]
    public void EveryCaseNamesAPairADocumentCanWrite()
    {
        foreach (var row in Cases())
        {
            var field = RuleFieldTable.Find(row.Field);

            Assert.NotNull(field);
            Assert.True(
                RuleFieldTable.Accepts(field!.Field, row.Operator),
                row.Field + " does not accept " + RuleOperatorTable.Of(row.Operator).Name);
        }
    }

    /// <summary>
    /// Every operator states its semantics in one sentence, which is the other half of the clause
    /// the theory above carries. Read off the table rather than listed here.
    /// </summary>
    [Fact]
    public void EveryOperatorStatesItsSemanticsInOneSentence()
    {
        foreach (var row in RuleOperatorTable.Rows)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Semantics), row.Name + " states no semantics.");
            Assert.EndsWith(".", row.Semantics.Trim(), StringComparison.Ordinal);
        }
    }
}
