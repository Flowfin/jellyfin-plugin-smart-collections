using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Evaluation;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The order a collection comes out in, and the cap a rule may put on it, asserted through a
/// document rather than over the comparison alone.
/// </summary>
/// <remarks>
/// A document is what an operator writes, so what is asserted here is what one of them gets. The
/// comparison's own arms - the shapes, the absence rule, the argument guards - are in
/// <see cref="ItemOrderTests"/>, because two of them cannot be reached through a document at all.
/// </remarks>
public class RuleOrderTests
{
    /// <summary>
    /// The instant every evaluation here is given, fixed for the reason the neighbouring suites
    /// fix one: a relative condition compiled against a clock asserts something different daily.
    /// </summary>
    private static readonly DateTimeOffset Given = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A rule that collects every film the library holds, so what is under test is the order and
    /// nothing about which items are in it.
    /// </summary>
    private const string EveryFilm = """
        {
            "schemaVersion": 1,
            "id": "every-film",
            "name": "Every film",
            "collects": ["movie"],
            SORT
            "match": { "allOf": [ { "field": "name", "operator": "notEquals", "value": "no film is called this" } ] }
        }
        """;

    /// <summary>
    /// The order a document declares is the order the answer comes out in, and the direction is
    /// the one the document wrote.
    /// </summary>
    /// <param name="direction">The direction the document declares.</param>
    /// <param name="expected">The titles, in the order the answer should hold them.</param>
    [Theory]
    [InlineData("ascending", new[] { "A Quiet Film", "The Job", "The Other Job" })]
    [InlineData("descending", new[] { "The Other Job", "The Job", "A Quiet Film" })]
    public void TheOrderIsTheOneTheDocumentDeclares(string direction, string[] expected)
    {
        var source = new FakeRuleItemSource { AnswersInReverse = true };
        Put(source, 1, "The Job", 1994);
        Put(source, 2, "A Quiet Film", 2001);
        Put(source, 3, "The Other Job", 1988);

        var answer = Evaluate(
            source,
            "\"sort\": [ { \"field\": \"name\", \"direction\": \"" + direction + "\" } ],");

        Assert.Equal(expected, Titles(source, answer));
    }

    /// <summary>
    /// A second term decides the items the first one left tied, and it decides them in its own
    /// direction rather than in the first term's.
    /// </summary>
    [Fact]
    public void ASecondTermDecidesWhatTheFirstLeftTied()
    {
        var source = new FakeRuleItemSource();
        Put(source, 1, "The Job", 1994);
        Put(source, 2, "A Quiet Film", 1994);
        Put(source, 3, "The Other Job", 1988);

        var answer = Evaluate(
            source,
            "\"sort\": [ { \"field\": \"productionYear\", \"direction\": \"descending\" },"
            + " { \"field\": \"name\", \"direction\": \"ascending\" } ],");

        Assert.Equal(["A Quiet Film", "The Job", "The Other Job"], Titles(source, answer));
    }

    /// <summary>
    /// The done condition this test carries: every sort ends with the identifier tie-break, so two
    /// items the declared terms cannot separate come out in one order however the server answered.
    /// </summary>
    /// <remarks>
    /// The three films share the year the document sorts on, so the declared term decides nothing
    /// at all and what is left is the tie-break. The fake answers in the reverse of the fill order
    /// on one run and in the fill order on the other, and the two answers are compared against the
    /// identifiers sorted as text rather than against a list written here, so the assertion says
    /// what the tie-break IS and not merely that it is stable.
    /// </remarks>
    [Fact]
    public void ATieIsBrokenByTheIdentifierAndNotByWhatTheServerAnsweredFirst()
    {
        var forwards = new FakeRuleItemSource();
        var backwards = new FakeRuleItemSource { AnswersInReverse = true };

        foreach (var source in new[] { forwards, backwards })
        {
            Put(source, 3, "The Other Job", 1994);
            Put(source, 1, "The Job", 1994);
            Put(source, 2, "A Quiet Film", 1994);
        }

        var sort = "\"sort\": [ { \"field\": \"productionYear\", \"direction\": \"ascending\" } ],";
        var one = Evaluate(forwards, sort);
        var other = Evaluate(backwards, sort);

        Assert.Equal(one, other);
        Assert.Equal(one.OrderBy(id => id.ToString("N", CultureInfo.InvariantCulture), StringComparer.Ordinal), one);
    }

    /// <summary>
    /// An item the library holds no value for sorts after every item that has one, and it does so
    /// in both directions rather than moving to the front when the order is reversed.
    /// </summary>
    /// <param name="direction">The direction the document declares.</param>
    [Theory]
    [InlineData("ascending")]
    [InlineData("descending")]
    public void AnItemWithNoValueForTheFieldSortsLastInBothDirections(string direction)
    {
        var source = new FakeRuleItemSource();
        Put(source, 1, "The Job", 1994);
        Put(source, 2, "A Quiet Film", 2001);
        source.Put(new Movie
        {
            Id = Id(3),
            Name = "The Other Job"
        });

        var answer = Evaluate(
            source,
            "\"sort\": [ { \"field\": \"productionYear\", \"direction\": \"" + direction + "\" } ],");

        Assert.Equal("The Other Job", Titles(source, answer).Last());
    }

    /// <summary>
    /// A cap takes the first items of the declared order and leaves the rest out.
    /// </summary>
    [Fact]
    public void ACapTakesTheFirstItemsOfTheDeclaredOrder()
    {
        var source = new FakeRuleItemSource { AnswersInReverse = true };
        Put(source, 1, "The Job", 1994);
        Put(source, 2, "A Quiet Film", 2001);
        Put(source, 3, "The Other Job", 1988);

        var answer = Evaluate(
            source,
            "\"limit\": 2, \"sort\": [ { \"field\": \"productionYear\", \"direction\": \"descending\" } ],");

        Assert.Equal(["A Quiet Film", "The Job"], Titles(source, answer));
    }

    /// <summary>
    /// A cap larger than what the rule collected takes everything rather than padding or throwing.
    /// </summary>
    [Fact]
    public void ACapLargerThanTheCollectionTakesEverything()
    {
        var source = new FakeRuleItemSource();
        Put(source, 1, "The Job", 1994);
        Put(source, 2, "A Quiet Film", 2001);

        var answer = Evaluate(
            source,
            "\"limit\": 50, \"sort\": [ { \"field\": \"name\", \"direction\": \"ascending\" } ],");

        Assert.Equal(2, answer.Count);
    }

    /// <summary>
    /// The cap is applied after the stage that runs over the query's answer, so it counts the
    /// items the rule collects rather than the items the server returned.
    /// </summary>
    /// <remarks>
    /// This is the difference between a cap and a page size, and it is the whole reason the cap is
    /// not pushed into the query. The rule's second condition is on the overview, which the
    /// server's query cannot carry, and the two films the query would have handed over first are
    /// both rejected by it. A cap applied to the query's answer would collect nothing here; the
    /// answer is the two films that satisfy the rule.
    /// </remarks>
    [Fact]
    public void TheCapCountsWhatTheRuleCollectsRatherThanWhatTheQueryReturned()
    {
        var source = new FakeRuleItemSource();

        foreach (var year in new[] { 2020, 2021 })
        {
            source.Put(new Movie
            {
                Id = Id(year - 2000),
                Name = "Bread " + year.ToString(CultureInfo.InvariantCulture),
                ProductionYear = year,
                Overview = "A quiet film about bread."
            });
        }

        foreach (var year in new[] { 1994, 1988 })
        {
            source.Put(new Movie
            {
                Id = Id(year - 1900),
                Name = "Job " + year.ToString(CultureInfo.InvariantCulture),
                ProductionYear = year,
                Overview = "A heist in three acts."
            });
        }

        var document = """
            {
                "schemaVersion": 1,
                "id": "heists",
                "name": "Heists",
                "collects": ["movie"],
                "limit": 2,
                "sort": [ { "field": "productionYear", "direction": "ascending" } ],
                "match": { "allOf": [ { "field": "overview", "operator": "contains", "value": "heist" } ] }
            }
            """;

        var evaluation = RuleEvaluator.Evaluate(Document(document), source, Given);

        Assert.True(evaluation.IsAccepted);
        Assert.Equal(["Job 1988", "Job 1994"], Titles(source, evaluation.ItemIds));
    }

    /// <summary>
    /// A document whose order is refused is refused by the evaluation too, rather than evaluated
    /// with the order dropped.
    /// </summary>
    /// <remarks>
    /// The document is built rather than read through the validator, because the validator refuses
    /// it for the same reason and this step is the one under test. That is the case the step's own
    /// refusals exist for: a caller may hand it a document it built itself.
    /// </remarks>
    [Fact]
    public void AnEvaluationOfADocumentWhoseOrderIsRefusedIsRefused()
    {
        var evaluation = RuleEvaluator.Evaluate(
            new RuleDocument(
                1,
                "every-film",
                "Every film",
                """
                {
                    "schemaVersion": 1,
                    "id": "every-film",
                    "name": "Every film",
                    "collects": ["movie"],
                    "sort": [ { "field": "genres", "direction": "ascending" } ],
                    "match": { "allOf": [ { "field": "name", "operator": "notEquals", "value": "no film is called this" } ] }
                }
                """),
            new FakeRuleItemSource(),
            Given);

        Assert.False(evaluation.IsAccepted);
        Assert.Equal("/sort/0/field", Assert.Single(evaluation.Errors).Pointer);
    }

    private static Guid Id(int seed)
        => Guid.Parse(seed.ToString("D8", CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000000");

    private static void Put(FakeRuleItemSource source, int seed, string name, int year)
        => source.Put(new Movie
        {
            Id = Id(seed),
            Name = name,
            ProductionYear = year
        });

    private static IReadOnlyList<Guid> Evaluate(FakeRuleItemSource source, string sort)
    {
        var evaluation = RuleEvaluator.Evaluate(
            Document(EveryFilm.Replace("SORT", sort, StringComparison.Ordinal)),
            source,
            Given);

        Assert.True(
            evaluation.IsAccepted,
            string.Join("; ", evaluation.Errors.Select(error => error.ToString())));

        return evaluation.ItemIds;
    }

    private static string[] Titles(FakeRuleItemSource source, IReadOnlyList<Guid> ids)
    {
        var byId = source.Select(new MediaBrowser.Controller.Entities.InternalItemsQuery())
            .ToDictionary(item => item.Id, item => item.Name);

        return ids.Select(id => byId[id]).ToArray();
    }

    private static RuleDocument Document(string text)
    {
        var validation = RuleDocumentValidator.Read(text);

        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Errors.Select(error => error.ToString())));

        return validation.Document!;
    }
}
