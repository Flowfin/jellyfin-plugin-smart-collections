using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Evaluation;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The stage that runs after the query: one item, one condition, one answer.
/// </summary>
/// <remarks>
/// Every pair a document can write is asserted in both directions here. The table below is
/// generated from nothing and is written out, which is the opposite of the sweeps elsewhere in
/// this suite and is deliberate: what a comparison MEANS is the thing under test, so a case
/// derived from the same table the comparison is written against would agree with it by
/// construction.
/// </remarks>
public class ConditionMatcherTests
{
    private static readonly DateTimeOffset Given = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// One string the library holds, compared every way a document may compare it.
    /// </summary>
    /// <param name="operatorName">The operator, as a document writes it.</param>
    /// <param name="written">The value or values the document wrote.</param>
    /// <param name="expected">The answer.</param>
    [Theory]
    [InlineData("equals", new[] { "PG-13" }, true)]
    [InlineData("equals", new[] { "pg-13" }, true)]
    [InlineData("equals", new[] { "R" }, false)]
    [InlineData("notEquals", new[] { "R" }, true)]
    [InlineData("notEquals", new[] { "PG-13" }, false)]
    [InlineData("in", new[] { "R", "PG-13" }, true)]
    [InlineData("in", new[] { "R", "PG" }, false)]
    [InlineData("notIn", new[] { "R", "PG" }, true)]
    [InlineData("notIn", new[] { "R", "PG-13" }, false)]
    [InlineData("isEmpty", new string[0], false)]
    [InlineData("isNotEmpty", new string[0], true)]
    public void AnAgeClassificationIsComparedAsOneString(string operatorName, string[] written, bool expected)
    {
        var item = new Movie { OfficialRating = "PG-13" };

        Assert.Equal(expected, Matches(item, "officialRating", operatorName, written));
    }

    /// <summary>
    /// A title, which is the other single string and carries the four operators an age
    /// classification does not.
    /// </summary>
    /// <param name="operatorName">The operator, as a document writes it.</param>
    /// <param name="written">The value the document wrote.</param>
    /// <param name="expected">The answer.</param>
    [Theory]
    [InlineData("contains", "quiet", true)]
    [InlineData("contains", "QUIET", true)]
    [InlineData("contains", "loud", false)]
    [InlineData("notContains", "loud", true)]
    [InlineData("notContains", "quiet", false)]
    [InlineData("startsWith", "A quiet", true)]
    [InlineData("startsWith", "quiet", false)]
    [InlineData("endsWith", "bread", true)]
    [InlineData("endsWith", "quiet", false)]
    public void ATitleIsComparedAsOneString(string operatorName, string written, bool expected)
    {
        var item = new Movie { Name = "A quiet film about bread" };

        Assert.Equal(expected, Matches(item, "name", operatorName, [written]));
    }

    /// <summary>
    /// The strings a list field holds are compared by membership rather than by substring, which
    /// is what the field table says <c>contains</c> means over a list.
    /// </summary>
    /// <param name="operatorName">The operator, as a document writes it.</param>
    /// <param name="written">The value the document wrote.</param>
    /// <param name="expected">The answer.</param>
    [Theory]
    [InlineData("contains", "Thriller", true)]
    [InlineData("contains", "thriller", true)]
    [InlineData("contains", "Thrill", false)]
    [InlineData("contains", "Comedy", false)]
    [InlineData("notContains", "Comedy", true)]
    [InlineData("notContains", "Thriller", false)]
    public void AListOfStringsIsComparedByMembership(string operatorName, string written, bool expected)
    {
        var item = new Movie { Genres = ["Thriller", "Crime"] };

        Assert.Equal(expected, Matches(item, "genres", operatorName, [written]));
    }

    /// <summary>
    /// A list the library holds nothing in is empty, and every comparison over it answers false.
    /// </summary>
    /// <param name="operatorName">The operator, as a document writes it.</param>
    /// <param name="expected">The answer.</param>
    [Theory]
    [InlineData("isEmpty", true)]
    [InlineData("isNotEmpty", false)]
    [InlineData("contains", false)]
    [InlineData("notContains", false)]
    public void AnEmptyListAnswersOnlyForTheTwoOperatorsAboutAbsence(string operatorName, bool expected)
    {
        var item = new Movie { Tags = [] };

        Assert.Equal(expected, Matches(item, "tags", operatorName, ["seen"]));
    }

    /// <summary>
    /// A number the library holds, compared every way. The two numeric fields declare different
    /// value types, a decimal and a whole number, and both reach the same comparison.
    /// </summary>
    /// <param name="field">The field, as a document writes it.</param>
    /// <param name="operatorName">The operator, as a document writes it.</param>
    /// <param name="written">The value the document wrote.</param>
    /// <param name="expected">The answer.</param>
    [Theory]
    [InlineData("communityRating", "greaterThan", "7.5", true)]
    [InlineData("communityRating", "greaterThan", "8.5", false)]
    [InlineData("communityRating", "greaterThanOrEqual", "8", true)]
    [InlineData("communityRating", "greaterThanOrEqual", "8.5", false)]
    [InlineData("communityRating", "lessThan", "9", true)]
    [InlineData("communityRating", "lessThan", "8", false)]
    [InlineData("communityRating", "lessThanOrEqual", "8", true)]
    [InlineData("communityRating", "lessThanOrEqual", "7.5", false)]
    [InlineData("productionYear", "equals", "1994", true)]
    [InlineData("productionYear", "equals", "1995", false)]
    [InlineData("productionYear", "notEquals", "1995", true)]
    [InlineData("productionYear", "notEquals", "1994", false)]
    public void ANumberIsCompared(string field, string operatorName, string written, bool expected)
    {
        var item = new Movie { CommunityRating = 8f, ProductionYear = 1994 };

        Assert.Equal(expected, Matches(item, field, operatorName, [written]));
    }

    /// <summary>
    /// The two list operators over a number, which are the arms the theory above cannot reach with
    /// one value beside them.
    /// </summary>
    /// <param name="operatorName">The operator, as a document writes it.</param>
    /// <param name="written">The values the document wrote.</param>
    /// <param name="expected">The answer.</param>
    [Theory]
    [InlineData("in", new[] { "1994", "2001" }, true)]
    [InlineData("in", new[] { "1995", "2001" }, false)]
    [InlineData("notIn", new[] { "1995", "2001" }, true)]
    [InlineData("notIn", new[] { "1994", "2001" }, false)]
    public void AYearIsComparedAgainstAList(string operatorName, string[] written, bool expected)
    {
        var item = new Movie { ProductionYear = 1994 };

        Assert.Equal(expected, Matches(item, "productionYear", operatorName, written));
    }

    /// <summary>
    /// An instant the library holds, compared every way, with the strict comparisons asserted at
    /// the boundary rather than away from it.
    /// </summary>
    /// <param name="operatorName">The operator, as a document writes it.</param>
    /// <param name="written">The value the document wrote.</param>
    /// <param name="expected">The answer.</param>
    [Theory]
    [InlineData("before", "1995-01-01T00:00:00Z", true)]
    [InlineData("before", "1994-06-01T00:00:00Z", false)]
    [InlineData("after", "1993-01-01T00:00:00Z", true)]
    [InlineData("after", "1994-06-01T00:00:00Z", false)]
    public void AnInstantIsCompared(string operatorName, string written, bool expected)
    {
        var item = new Movie { PremiereDate = new DateTime(1994, 6, 1, 0, 0, 0, DateTimeKind.Utc) };

        Assert.Equal(expected, Matches(item, "premiereDate", operatorName, [written]));
    }

    /// <summary>
    /// The span <c>withinLast</c> names is closed at both ends and ends at the instant the
    /// evaluation was given, which is what the compiler writes into the server's own query for the
    /// pair it carries. Asserted at both ends and one tick past the floor.
    /// </summary>
    /// <param name="added">When the server first saw the item.</param>
    /// <param name="expected">The answer.</param>
    [Theory]
    [InlineData("2025-12-16T00:00:00Z", true)]
    [InlineData("2026-01-15T00:00:00Z", true)]
    [InlineData("2025-12-15T23:59:59Z", false)]
    [InlineData("2026-01-15T00:00:01Z", false)]
    public void ASpanEndingAtTheInstantIsClosedAtBothEnds(string added, bool expected)
    {
        var item = new Movie { DateCreated = DateTime.Parse(added, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime() };

        Assert.Equal(expected, Matches(item, "dateAdded", "withinLast", ["P30D"]));
    }

    /// <summary>
    /// A length of time the library holds, compared every way.
    /// </summary>
    /// <param name="operatorName">The operator, as a document writes it.</param>
    /// <param name="written">The value the document wrote.</param>
    /// <param name="expected">The answer.</param>
    [Theory]
    [InlineData("greaterThan", "PT1H30M", true)]
    [InlineData("greaterThan", "PT2H", false)]
    [InlineData("greaterThanOrEqual", "PT2H", true)]
    [InlineData("greaterThanOrEqual", "PT2H1M", false)]
    [InlineData("lessThan", "PT3H", true)]
    [InlineData("lessThan", "PT2H", false)]
    [InlineData("lessThanOrEqual", "PT2H", true)]
    [InlineData("lessThanOrEqual", "PT1H", false)]
    public void ALengthOfTimeIsCompared(string operatorName, string written, bool expected)
    {
        var item = new Movie { RunTimeTicks = TimeSpan.FromHours(2).Ticks };

        Assert.Equal(expected, Matches(item, "runtime", operatorName, [written]));
    }

    /// <summary>
    /// The decision this stage takes about absence, asserted in both directions on every shape
    /// that can be absent: a value the library does not hold satisfies no comparison, and the two
    /// operators about absence are the only ones that ever answer true for one.
    /// </summary>
    /// <param name="field">The field, as a document writes it.</param>
    /// <param name="operatorName">The operator, as a document writes it.</param>
    /// <param name="written">The value the document wrote.</param>
    [Theory]
    [InlineData("officialRating", "equals", "PG-13")]
    [InlineData("officialRating", "notEquals", "PG-13")]
    [InlineData("officialRating", "in", "PG-13")]
    [InlineData("officialRating", "notIn", "PG-13")]
    [InlineData("overview", "contains", "heist")]
    [InlineData("overview", "notContains", "heist")]
    [InlineData("overview", "startsWith", "A")]
    [InlineData("overview", "endsWith", "acts.")]
    [InlineData("communityRating", "greaterThan", "5")]
    [InlineData("communityRating", "lessThan", "5")]
    [InlineData("productionYear", "equals", "1994")]
    [InlineData("premiereDate", "before", "1995-01-01T00:00:00Z")]
    [InlineData("premiereDate", "after", "1993-01-01T00:00:00Z")]
    [InlineData("premiereDate", "withinLast", "P30D")]
    [InlineData("runtime", "greaterThan", "PT1H")]
    [InlineData("runtime", "lessThanOrEqual", "PT4H")]
    public void AValueTheLibraryDoesNotHoldSatisfiesNoComparison(string field, string operatorName, string written)
    {
        Assert.False(Matches(new Movie(), field, operatorName, [written]));
    }

    /// <summary>
    /// And the two operators about absence answer from the absence itself, which is what makes the
    /// theory above a decision rather than every comparison quietly failing.
    /// </summary>
    [Fact]
    public void TheTwoOperatorsAboutAbsenceAnswerForAnAbsentValue()
    {
        Assert.True(Matches(new Movie(), "officialRating", "isEmpty", []));
        Assert.False(Matches(new Movie(), "officialRating", "isNotEmpty", []));
    }

    /// <summary>
    /// A string holding nothing but spaces is absent rather than present and empty. The library
    /// leaves a field unset in three ways and an operator writing <c>isEmpty</c> means all three.
    /// </summary>
    [Fact]
    public void AStringOfWhitespaceIsAbsent()
    {
        Assert.True(Matches(new Movie { OfficialRating = "   " }, "officialRating", "isEmpty", []));
        Assert.True(Matches(new Movie { OfficialRating = string.Empty }, "officialRating", "isEmpty", []));
    }

    /// <summary>
    /// A field with no arm is a fault in this file rather than in a document, and it is refused
    /// rather than answered.
    /// </summary>
    [Fact]
    public void AFieldWithNoArmIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ItemFieldReader.Read(new Movie(), (RuleField)9999));
    }

    /// <summary>
    /// Neither argument of either entry point may be absent.
    /// </summary>
    [Fact]
    public void TheStageRefusesAnArgumentThatIsNotThere()
    {
        var condition = Condition("name", "equals", ["a"]);

        Assert.Throws<ArgumentNullException>(() => ConditionMatcher.Matches(null!, condition, Given));
        Assert.Throws<ArgumentNullException>(() => ConditionMatcher.Matches(new Movie(), null!, Given));
        Assert.Throws<ArgumentNullException>(
            () => ConditionMatcher.Compare(null!, RuleOperator.Equals, [], Given));
        Assert.Throws<ArgumentNullException>(
            () => ConditionMatcher.Compare(ItemFieldReading.OfText("a"), RuleOperator.Equals, null!, Given));
    }

    /// <summary>
    /// Every shape refuses an operator it has no comparison for, and the pair reaching it is one
    /// the vocabulary tables refuse before an evaluation could produce it. The fixture pairs a
    /// real field row with a real operator row that the field's own row does not declare, which is
    /// the smallest thing that reaches the arm.
    /// </summary>
    /// <param name="field">The field whose shape is under test.</param>
    /// <param name="operator">An operator no arm of that shape answers.</param>
    [Theory]
    [InlineData(RuleField.Name, RuleOperator.GreaterThan)]
    [InlineData(RuleField.Genres, RuleOperator.Equals)]
    [InlineData(RuleField.ProductionYear, RuleOperator.Contains)]
    [InlineData(RuleField.PremiereDate, RuleOperator.Equals)]
    [InlineData(RuleField.Runtime, RuleOperator.Equals)]
    public void AnOperatorNoArmOfTheShapeAnswersIsRefused(RuleField field, RuleOperator @operator)
    {
        var item = new Movie
        {
            Name = "A film",
            Genres = ["Thriller"],
            ProductionYear = 1994,
            PremiereDate = new DateTime(1994, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            RunTimeTicks = TimeSpan.FromHours(2).Ticks
        };

        Assert.False(
            RuleFieldTable.Accepts(field, @operator),
            "The fixture has to pair a field with an operator its own row does not declare.");

        var condition = new RuleConditionValue(
            "/match/allOf/0",
            RuleFieldTable.Of(field),
            RuleOperatorTable.Of(@operator),
            [RuleValue.Of(RuleValueType.String, "anything")]);

        Assert.Throws<ArgumentOutOfRangeException>(() => ConditionMatcher.Matches(item, condition, Given));
    }

    /// <summary>
    /// The last arm of the shape dispatch, reached with a reading of a shape nothing produces.
    /// Without this the arm ships unproven, which is refused in this repository.
    /// </summary>
    [Fact]
    public void AShapeNoReadingProducesIsRefused()
    {
        var reading = new ItemFieldReading(
            (ItemFieldShape)9999,
            true,
            null,
            [],
            0m,
            default,
            default);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConditionMatcher.Compare(reading, RuleOperator.Equals, [], Given));
    }

    /// <summary>
    /// Every shape the vocabulary can produce has an arm, read off the field table rather than
    /// listed here. A field added whose shape nothing compares would otherwise reach the arm above
    /// at evaluation rather than here.
    /// </summary>
    [Fact]
    public void EveryFieldTheVocabularyDeclaresReadsToAShapeSomethingCompares()
    {
        var item = new Movie
        {
            Name = "A film",
            OfficialRating = "PG-13",
            Overview = "Words",
            Genres = ["Thriller"],
            Tags = ["seen"],
            CommunityRating = 8f,
            ProductionYear = 1994,
            PremiereDate = new DateTime(1994, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            DateCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RunTimeTicks = TimeSpan.FromHours(2).Ticks
        };

        var shapes = RuleFieldTable.Rows
            .Select(row => ItemFieldReader.Read(item, row.Field).Shape)
            .Distinct()
            .OrderBy(shape => shape)
            .ToArray();

        Assert.Equal(Enum.GetValues<ItemFieldShape>(), shapes);
    }

    /// <summary>
    /// An instant the library holds with no kind is read as UTC, so the same item answers the same
    /// way on two servers in two zones.
    /// </summary>
    [Fact]
    public void AnInstantWithNoKindIsReadAsUniversalTime()
    {
        var unspecified = new Movie { PremiereDate = new DateTime(1994, 6, 1, 0, 0, 0, DateTimeKind.Unspecified) };
        var universal = new Movie { PremiereDate = new DateTime(1994, 6, 1, 0, 0, 0, DateTimeKind.Utc) };

        Assert.Equal(
            ItemFieldReader.Read(universal, RuleField.PremiereDate).Instant,
            ItemFieldReader.Read(unspecified, RuleField.PremiereDate).Instant);
    }

    /// <summary>
    /// An instant the library holds in local time is converted rather than relabelled.
    /// </summary>
    [Fact]
    public void AnInstantInLocalTimeIsConverted()
    {
        var local = new DateTime(1994, 6, 1, 12, 0, 0, DateTimeKind.Local);

        Assert.Equal(
            new DateTimeOffset(local.ToUniversalTime(), TimeSpan.Zero),
            ItemFieldReader.Read(new Movie { PremiereDate = local }, RuleField.PremiereDate).Instant);
    }

    /// <summary>
    /// A list the library holds as a null reads as none rather than throwing.
    /// </summary>
    /// <remarks>
    /// The null is written into the item on purpose. A freshly constructed item carries an empty
    /// array rather than a null, so an item built the ordinary way never reaches the guard, and a
    /// guard nothing reaches is one this repository refuses to ship. What the server hands back
    /// out of its own store is not built by this constructor, and the two arms are both here.
    /// </remarks>
    [Fact]
    public void AListTheLibraryHoldsAsNothingReadsAsNone()
    {
        var empty = new Movie();
        var absent = new Movie { Genres = null! };

        Assert.Empty(ItemFieldReader.Read(empty, RuleField.Genres).TextList);
        Assert.False(ItemFieldReader.Read(empty, RuleField.Genres).IsPresent);
        Assert.Empty(ItemFieldReader.Read(absent, RuleField.Genres).TextList);
        Assert.False(ItemFieldReader.Read(absent, RuleField.Genres).IsPresent);
    }

    /// <summary>
    /// The reading refuses a list that is not there, which is the one argument any of its
    /// factories takes that could be.
    /// </summary>
    [Fact]
    public void TheReadingRefusesAListThatIsNotThere()
    {
        Assert.Throws<ArgumentNullException>(() => ItemFieldReading.OfTextList(null!));
    }

    /// <summary>
    /// The fake source refuses either argument that is not there, so a test handing it nothing
    /// fails where it wrote the mistake.
    /// </summary>
    [Fact]
    public void TheFakeSourceRefusesAnArgumentThatIsNotThere()
    {
        var source = new FakeRuleItemSource();

        Assert.Throws<ArgumentNullException>(() => source.Put(null!));
        Assert.Throws<ArgumentNullException>(() => source.Select(null!));
    }

    /// <summary>
    /// Builds one condition by writing the document that carries it and reading it back through
    /// the stages the plugin itself reads a rule with.
    /// </summary>
    /// <param name="field">The field, as a document writes it.</param>
    /// <param name="operatorName">The operator, as a document writes it.</param>
    /// <param name="written">The values the document wrote.</param>
    /// <returns>The condition.</returns>
    /// <remarks>
    /// Through the document rather than by constructing the value, because the type a value is
    /// parsed against depends on the field and the operator together, and a test that decided that
    /// itself would be asserting against its own answer rather than against the vocabulary's.
    /// </remarks>
    private static RuleConditionValue Condition(string field, string operatorName, IReadOnlyList<string> written)
    {
        var fieldRow = RuleFieldTable.Find(field)!;
        var operatorRow = RuleOperatorTable.Find(operatorName)!;
        var value = Json(fieldRow.ValueType, operatorRow, written);
        var text = "{\"schemaVersion\":1,\"id\":\"x\",\"name\":\"X\",\"collects\":[\"movie\"],"
                   + "\"match\":{\"allOf\":[{\"field\":\"" + field + "\",\"operator\":\"" + operatorName + "\""
                   + value + "}]}}";

        using var parsed = JsonDocument.Parse(text);
        var root = parsed.RootElement;
        var scope = RuleItemScopeReader.Read(root);
        var composition = RuleCompositionReader.Read(root.GetProperty("match"), "/match");
        var fields = RuleFieldReader.Read(root, composition.Group!, scope.Kinds);
        var operators = RuleOperatorReader.Read(root, fields.Fields);
        var values = RuleValueReader.Read(root, operators.Operators);

        Assert.True(
            values.IsAccepted,
            "The fixture is refused: " + string.Join("; ", values.Errors.Select(error => error.ToString())));

        return Assert.Single(values.Conditions);
    }

    /// <summary>
    /// The <c>value</c> member a document writes beside an operator, or nothing where the operator
    /// takes none.
    /// </summary>
    /// <param name="type">The field's declared type.</param>
    /// <param name="operatorRow">The operator's row.</param>
    /// <param name="written">The values, as a test wrote them.</param>
    /// <returns>The member, with its leading comma, or an empty string.</returns>
    private static string Json(RuleValueType type, RuleOperatorRow operatorRow, IReadOnlyList<string> written)
    {
        if (!operatorRow.TakesAValue)
        {
            return string.Empty;
        }

        var bare = type is RuleValueType.Integer or RuleValueType.Decimal or RuleValueType.Boolean;
        var members = written.Select(value => bare ? value : "\"" + value + "\"").ToArray();

        return operatorRow.TakesAList
            ? ",\"value\":[" + string.Join(",", members) + "]"
            : ",\"value\":" + members[0];
    }

    /// <summary>
    /// Whether an item satisfies a condition written the way a document writes one.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="field">The field, as a document writes it.</param>
    /// <param name="operatorName">The operator, as a document writes it.</param>
    /// <param name="written">The values the document wrote.</param>
    /// <returns>The answer.</returns>
    private static bool Matches(BaseItem item, string field, string operatorName, IReadOnlyList<string> written)
        => ConditionMatcher.Matches(item, Condition(field, operatorName, written), Given);
}
