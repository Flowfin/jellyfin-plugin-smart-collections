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
/// The step that takes a compiled rule to the server and produces the ordered identifier list a
/// refresh acts on.
/// </summary>
/// <remarks>
/// Every test here runs against <see cref="FakeRuleItemSource"/> and no server. That is the whole
/// argument for the port existing: an evaluation is the part of this plugin most worth asserting
/// on, and the surface it needs from the server is one query and a list of items.
/// </remarks>
public class RuleEvaluatorTests
{
    /// <summary>
    /// The instant every evaluation here is given. Fixed rather than read from the machine, for
    /// the reason the corpus fixes its own: a relative condition compiled against a clock produces
    /// a test that asserts something different every day.
    /// </summary>
    private static readonly DateTimeOffset Given = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The document the wider-query test rests on. Its query narrows on the age classification,
    /// which the server's own query carries, and its second condition is on the overview, which it
    /// does not - so the query is deliberately wider than the rule.
    /// </summary>
    private const string WiderThanTheRule = """
        {
            "schemaVersion": 1,
            "id": "heists",
            "name": "Heists",
            "collects": ["movie"],
            "match": {
                "allOf": [
                    { "field": "officialRating", "operator": "equals", "value": "PG-13" },
                    { "field": "overview", "operator": "contains", "value": "heist" }
                ]
            }
        }
        """;

    /// <summary>
    /// The done condition this test carries: the step applies every condition the compiler handed
    /// back, proved by a rule whose query is wider than it.
    /// </summary>
    /// <remarks>
    /// The two items differ in the overview alone, so both satisfy the pushed condition and the
    /// server would answer with both. What separates them is the condition the query could not
    /// carry, which is the one this step has to apply.
    /// </remarks>
    [Fact]
    public void TheItemTheQueryReturnsAndTheRuleDoesNotIsAbsentFromTheAnswer()
    {
        var source = new FakeRuleItemSource();
        var wanted = source.Put(new Movie
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "The Job",
            OfficialRating = "PG-13",
            Overview = "A heist in three acts."
        });
        source.Put(new Movie
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "The Other Job",
            OfficialRating = "PG-13",
            Overview = "A quiet film about bread."
        });

        var evaluation = RuleEvaluator.Evaluate(Document(WiderThanTheRule), source, Given);

        Assert.True(evaluation.IsAccepted);
        Assert.Equal([wanted.Id], evaluation.ItemIds);
    }

    /// <summary>
    /// The query the step asks carries the condition the server can answer, so the post-query
    /// stage is the second half of the rule and not the whole of it. Without this the test above
    /// passes over a step that asked for the library and filtered everything in the plugin.
    /// </summary>
    [Fact]
    public void TheConditionTheServerCanAnswerIsAskedOfTheServer()
    {
        var source = new FakeRuleItemSource();

        RuleEvaluator.Evaluate(Document(WiderThanTheRule), source, Given);

        var query = Assert.Single(source.Asked);
        Assert.Equal(["PG-13"], query.OfficialRatings);
    }

    /// <summary>
    /// The done condition this test carries: the answer is ordered, and it is the same order
    /// whichever order the server answered in.
    /// </summary>
    [Fact]
    public void TheAnswerIsOrderedByIdentifierWhateverOrderTheServerAnsweredIn()
    {
        var forwards = RuleEvaluator.Evaluate(Document(EveryFilm), Filled(false), Given);
        var backwards = RuleEvaluator.Evaluate(Document(EveryFilm), Filled(true), Given);

        Assert.Equal(
            [
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-1111-1111-1111-111111111111"),
                Guid.Parse("33333333-1111-1111-1111-111111111111")
            ],
            forwards.ItemIds);
        Assert.Equal(forwards.ItemIds, backwards.ItemIds);
    }

    /// <summary>
    /// The done condition this test carries: the instant in the answer is the one passed in rather
    /// than one read anywhere.
    /// </summary>
    [Fact]
    public void TheInstantInTheAnswerIsTheOneThatWasPassedIn()
    {
        var given = new DateTimeOffset(1999, 3, 4, 5, 6, 7, TimeSpan.FromHours(2));

        var evaluation = RuleEvaluator.Evaluate(Document(EveryFilm), Filled(false), given);

        Assert.Equal(given, evaluation.EvaluatedAt);
    }

    /// <summary>
    /// The same, on the answer a refusal produces. A refusal is a thing that happened at a moment
    /// as much as an acceptance is, and a report that carried the instant on one and not the other
    /// would be reproducible only half the time.
    /// </summary>
    [Fact]
    public void ARefusalCarriesTheInstantToo()
    {
        var given = new DateTimeOffset(1999, 3, 4, 5, 6, 7, TimeSpan.FromHours(2));

        var evaluation = RuleEvaluator.Evaluate(Document(TwoConditionsOnOneProperty), new FakeRuleItemSource(), given);

        Assert.False(evaluation.IsAccepted);
        Assert.Equal(given, evaluation.EvaluatedAt);
    }

    /// <summary>
    /// The done condition this test carries: a refused compilation produces no query to the server
    /// at all.
    /// </summary>
    /// <remarks>
    /// A refused compilation carries an unnarrowed query, so a step that asked it anyway would ask
    /// a real server for every film it holds on behalf of a rule that does not compile. That is the
    /// failure this asserts against, and it asserts on the fake never being asked rather than on
    /// the answer being empty: an empty answer is what a narrowed query over an empty library gives
    /// too.
    /// </remarks>
    [Fact]
    public void ARefusedCompilationAsksTheServerNothing()
    {
        var source = new FakeRuleItemSource();

        var evaluation = RuleEvaluator.Evaluate(Document(TwoConditionsOnOneProperty), source, Given);

        Assert.False(evaluation.IsAccepted);
        Assert.Empty(evaluation.ItemIds);
        Assert.Empty(source.Asked);
        Assert.Single(evaluation.Errors);
    }

    /// <summary>
    /// A disjunction pushes nothing into the query, because a server query is a conjunction and
    /// one arm of an <c>anyOf</c> does not have to hold. A step that pushed one arm anyway would
    /// ask the server for that arm's items and drop every item the other arm collects, which is a
    /// rule quietly meaning something narrower than it says.
    /// </summary>
    [Fact]
    public void NeitherArmOfADisjunctionNarrowsTheQueryAndBothArmsStillCollect()
    {
        var source = new FakeRuleItemSource();
        source.Put(new Movie
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Rated",
            OfficialRating = "PG-13"
        });
        source.Put(new Movie
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "Aged",
            ProductionYear = 1994
        });
        source.Put(new Movie
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "Neither",
            ProductionYear = 2001
        });

        var evaluation = RuleEvaluator.Evaluate(Document(EitherArm), source, Given);

        var query = Assert.Single(source.Asked);
        Assert.Empty(query.OfficialRatings);
        Assert.Empty(query.Years);
        Assert.Equal(
            [
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222")
            ],
            evaluation.ItemIds);
    }

    /// <summary>
    /// A negation collects what its members do not match, and nothing under it reaches the query
    /// for the reason the disjunction's arms do not.
    /// </summary>
    [Fact]
    public void ANegationCollectsWhatItsMembersDoNotMatch()
    {
        var source = new FakeRuleItemSource();
        source.Put(new Movie
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Tagged",
            Tags = ["seen"]
        });
        source.Put(new Movie
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "Untagged",
            Tags = []
        });

        var evaluation = RuleEvaluator.Evaluate(Document(NotTagged), source, Given);

        Assert.Equal([Guid.Parse("22222222-2222-2222-2222-222222222222")], evaluation.ItemIds);
        Assert.Empty(Assert.Single(source.Asked).Tags);
    }

    /// <summary>
    /// A nested conjunction is still a conjunction, so its conditions reach the query. Without
    /// this the walk that collects them could stop at the root and every test above would still
    /// pass.
    /// </summary>
    [Fact]
    public void AConditionInsideANestedConjunctionStillNarrowsTheQuery()
    {
        var source = new FakeRuleItemSource();

        RuleEvaluator.Evaluate(Document(NestedConjunction), source, Given);

        var query = Assert.Single(source.Asked);
        Assert.Equal(["PG-13"], query.OfficialRatings);
        Assert.Equal([1994], query.Years);
    }

    /// <summary>
    /// A group inside a group is walked when the items are judged, and both of its answers decide
    /// what the group holding it says. The disjunction here holds one condition and one nested
    /// group, so an item satisfies it through the nested group alone or through neither.
    /// </summary>
    [Fact]
    public void ANestedGroupIsWalkedWhenAnItemIsJudged()
    {
        var source = new FakeRuleItemSource();
        source.Put(new Movie
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Through the nested group",
            Overview = "A heist in three acts.",
            RunTimeTicks = TimeSpan.FromHours(3).Ticks
        });
        source.Put(new Movie
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "Through the condition beside it",
            Tags = ["seen"]
        });
        source.Put(new Movie
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "Through neither",
            Overview = "A heist in three acts.",
            RunTimeTicks = TimeSpan.FromMinutes(20).Ticks
        });

        var evaluation = RuleEvaluator.Evaluate(Document(ANestedGroup), source, Given);

        Assert.Equal(
            [
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222")
            ],
            evaluation.ItemIds);
    }

    /// <summary>
    /// The scope bounds the query whatever the rule says, which is the compiler's property and is
    /// asserted here because this step is what hands it the scope.
    /// </summary>
    [Fact]
    public void TheQueryIsBoundedByTheScopeTheDocumentDeclares()
    {
        var source = new FakeRuleItemSource();

        RuleEvaluator.Evaluate(Document(EveryFilm), source, Given);

        Assert.Equal([Jellyfin.Data.Enums.BaseItemKind.Movie], Assert.Single(source.Asked).IncludeItemTypes);
    }

    /// <summary>
    /// A document with no rule in it is refused rather than read as a rule collecting everything
    /// the scope names. Nothing the store hands out can be in that state, because validation
    /// refuses it; a caller building a document itself can be, and the answer is a refusal rather
    /// than a throw because that is the shape this step already has for one.
    /// </summary>
    [Fact]
    public void ADocumentWithNoRuleIsRefusedAndAsksTheServerNothing()
    {
        var source = new FakeRuleItemSource();
        var document = new RuleDocument(
            1,
            "empty",
            "Empty",
            """{ "schemaVersion": 1, "id": "empty", "name": "Empty", "collects": ["movie"] }""");

        var evaluation = RuleEvaluator.Evaluate(document, source, Given);

        Assert.False(evaluation.IsAccepted);
        Assert.Empty(source.Asked);
        Assert.Equal("/match", Assert.Single(evaluation.Errors).Pointer);
    }

    /// <summary>
    /// Each stage that reads a rule out of the document answers with its refusal rather than
    /// throwing, and each is reachable through a document a caller built rather than loaded.
    /// </summary>
    /// <param name="text">The document.</param>
    /// <param name="pointer">Where the refusal points.</param>
    [Theory]
    [InlineData("""{ "schemaVersion": 1, "id": "x", "name": "X", "collects": ["nothing"], "match": { "allOf": [] } }""", "/collects/0")]
    [InlineData("""{ "schemaVersion": 1, "id": "x", "name": "X", "collects": ["movie"], "match": { "allOf": [] } }""", "/match/allOf")]
    [InlineData("""{ "schemaVersion": 1, "id": "x", "name": "X", "collects": ["movie"], "match": { "allOf": [ { "field": "nope", "operator": "equals", "value": "a" } ] } }""", "/match/allOf/0/field")]
    [InlineData("""{ "schemaVersion": 1, "id": "x", "name": "X", "collects": ["movie"], "match": { "allOf": [ { "field": "name", "operator": "before", "value": "a" } ] } }""", "/match/allOf/0/operator")]
    [InlineData("""{ "schemaVersion": 1, "id": "x", "name": "X", "collects": ["movie"], "match": { "allOf": [ { "field": "productionYear", "operator": "equals", "value": "not a year" } ] } }""", "/match/allOf/0/value")]
    public void AStageThatRefusesAnswersWithItsReasonAndAsksTheServerNothing(string text, string pointer)
    {
        var source = new FakeRuleItemSource();

        var evaluation = RuleEvaluator.Evaluate(new RuleDocument(1, "x", "X", text), source, Given);

        Assert.False(evaluation.IsAccepted);
        Assert.Empty(source.Asked);
        Assert.Contains(evaluation.Errors, error => string.Equals(error.Pointer, pointer, StringComparison.Ordinal));
    }

    /// <summary>
    /// Neither argument may be absent, and the refusal is at the call rather than at the first use.
    /// </summary>
    [Fact]
    public void TheStepRefusesAnArgumentThatIsNotThere()
    {
        Assert.Throws<ArgumentNullException>(
            () => RuleEvaluator.Evaluate(null!, new FakeRuleItemSource(), Given));
        Assert.Throws<ArgumentNullException>(
            () => RuleEvaluator.Evaluate(Document(EveryFilm), null!, Given));
    }

    private const string EveryFilm = """
        {
            "schemaVersion": 1,
            "id": "every-film",
            "name": "Every film",
            "collects": ["movie"],
            "match": { "allOf": [ { "field": "tags", "operator": "isEmpty" } ] }
        }
        """;

    private const string TwoConditionsOnOneProperty = """
        {
            "schemaVersion": 1,
            "id": "two-writes",
            "name": "Two writes",
            "collects": ["movie"],
            "match": {
                "allOf": [
                    { "field": "officialRating", "operator": "equals", "value": "PG-13" },
                    { "field": "officialRating", "operator": "in", "value": ["R", "PG"] }
                ]
            }
        }
        """;

    private const string EitherArm = """
        {
            "schemaVersion": 1,
            "id": "either",
            "name": "Either",
            "collects": ["movie"],
            "match": {
                "anyOf": [
                    { "field": "officialRating", "operator": "equals", "value": "PG-13" },
                    { "field": "productionYear", "operator": "equals", "value": 1994 }
                ]
            }
        }
        """;

    private const string NotTagged = """
        {
            "schemaVersion": 1,
            "id": "not-tagged",
            "name": "Not tagged",
            "collects": ["movie"],
            "match": {
                "noneOf": [
                    { "field": "tags", "operator": "contains", "value": "seen" }
                ]
            }
        }
        """;

    private const string ANestedGroup = """
        {
            "schemaVersion": 1,
            "id": "nested-group",
            "name": "A nested group",
            "collects": ["movie"],
            "match": {
                "anyOf": [
                    { "field": "tags", "operator": "contains", "value": "seen" },
                    {
                        "allOf": [
                            { "field": "overview", "operator": "contains", "value": "heist" },
                            { "field": "runtime", "operator": "greaterThan", "value": "PT2H" }
                        ]
                    }
                ]
            }
        }
        """;

    private const string NestedConjunction = """
        {
            "schemaVersion": 1,
            "id": "nested",
            "name": "Nested",
            "collects": ["movie"],
            "match": {
                "allOf": [
                    { "field": "officialRating", "operator": "equals", "value": "PG-13" },
                    {
                        "allOf": [
                            { "field": "productionYear", "operator": "equals", "value": 1994 }
                        ]
                    }
                ]
            }
        }
        """;

    /// <summary>
    /// A document the validator accepted, which is the only state this step is handed one in
    /// through the store. Reading it here rather than constructing the record keeps every fixture
    /// above a document somebody could write.
    /// </summary>
    /// <param name="text">The document.</param>
    /// <returns>The accepted document.</returns>
    private static RuleDocument Document(string text)
    {
        var validation = RuleDocumentValidator.Read(text);

        Assert.True(
            validation.IsValid,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The fixture is refused by validation: {string.Join("; ", validation.Errors.Select(error => error.ToString()))}"));

        return validation.Document!;
    }

    /// <summary>
    /// Three films, in one order or the other.
    /// </summary>
    /// <param name="reversed">Whether the source answers in reverse.</param>
    /// <returns>The source.</returns>
    private static FakeRuleItemSource Filled(bool reversed)
    {
        var source = new FakeRuleItemSource { AnswersInReverse = reversed };

        foreach (var id in new[] { "22222222", "11111111", "33333333" })
        {
            source.Put(new Movie
            {
                Id = Guid.Parse(id + "-1111-1111-1111-111111111111"),
                Name = "Film " + id
            });
        }

        return source;
    }

    /// <summary>
    /// Without this the sweeps above could be reading an empty vocabulary and still pass.
    /// </summary>
    [Fact]
    public void TheFixturesAboveAreDocumentsTheValidatorAccepts()
    {
        var accepted = new List<string>
        {
            Document(WiderThanTheRule).Id,
            Document(EveryFilm).Id,
            Document(TwoConditionsOnOneProperty).Id,
            Document(EitherArm).Id,
            Document(NotTagged).Id,
            Document(NestedConjunction).Id
        };

        Assert.Equal(6, accepted.Count);
        Assert.Equal(accepted.Count, accepted.Distinct(StringComparer.Ordinal).Count());
    }
}
