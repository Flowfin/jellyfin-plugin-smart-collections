using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A condition on a field that means nothing for anything the rule collects is refused, and the
/// message names both.
/// </summary>
/// <remarks>
/// **NO DOCUMENT ANYBODY CAN WRITE TODAY REACHES THIS REFUSAL**, and that is stated here rather
/// than left for a reader to work out. Every field the vocabulary declares applies to both item
/// kinds a rule may collect, which <see cref="EveryDeclaredFieldAppliesToEveryKindARuleMayCollect"/>
/// asserts rather than asserts around, so the guard cannot fire on a real document and a test
/// written against one would prove nothing.
///
/// What it is proved against instead is a FIXTURE ROW that applies to one kind. The row exists for
/// the length of a test, is never in <see cref="RuleFieldTable"/>, and is reachable only because
/// the engine names this assembly in an <c>InternalsVisibleTo</c> whose own comment argues for it.
/// A fixture vocabulary is a fixture vocabulary: nothing here says anything about which fields
/// this plugin declares.
///
/// THE ALTERNATIVE WAS REFUSED ON #69 ON 2026-09-04. Widening the kinds a rule may collect until
/// some declared field means nothing for one of them - the runtime of a photo album is the case -
/// would change what a rule collects in order to give a refusal something to bite on, and every
/// row of the field table with it. Narrowing a field to the kinds it means is the answer taken,
/// and today that narrows none of them.
/// </remarks>
public class RuleFieldScopeTests
{
    /// <summary>
    /// A field that means something for a series and nothing for a film. It is not in the
    /// vocabulary and no document can name it; what it is for is to give the refusal a subject.
    /// </summary>
    private static RuleFieldRow SeriesOnly { get; } = new(
        RuleField.ProductionYear,
        "seasonCount",
        RuleValueType.Integer,
        [RuleOperator.Equals],
        [RuleItemKind.Series],
        null,
        "How many seasons the series has.");

    private static IReadOnlyList<RuleItemKindRow> Scope(params RuleItemKind[] kinds)
        => kinds.Select(RuleItemKindTable.Of).ToArray();

    /// <summary>
    /// The reason the guard needs a fixture at all. This is the sentence the remarks above rest
    /// on, and it stops being true on the day a field is narrowed, which is the day somebody
    /// should read this file again.
    /// </summary>
    [Fact]
    public void EveryDeclaredFieldAppliesToEveryKindARuleMayCollect()
    {
        foreach (var row in RuleFieldTable.Rows)
        {
            foreach (var kind in RuleItemKindTable.Rows)
            {
                Assert.True(
                    row.AppliesTo(kind.Kind),
                    "The field " + row.Name + " does not apply to " + kind.Name + ".");
            }
        }
    }

    /// <summary>
    /// The fixture is genuinely narrow, which is the other way every assertion below could pass
    /// without asserting anything.
    /// </summary>
    [Fact]
    public void TheFixtureRowAppliesToOneKindAndIsNotInTheVocabulary()
    {
        Assert.True(SeriesOnly.AppliesTo(RuleItemKind.Series));
        Assert.False(SeriesOnly.AppliesTo(RuleItemKind.Movie));
        Assert.Null(RuleFieldTable.Find(SeriesOnly.Name));
        Assert.DoesNotContain(SeriesOnly, RuleFieldTable.Rows);
    }

    /// <summary>
    /// The done condition this test carries, first half: the field means nothing for anything the
    /// rule collects, so it is refused.
    /// </summary>
    [Fact]
    public void AFieldThatMeansNothingForAnythingTheRuleCollectsIsOutsideTheScope()
        => Assert.False(RuleFieldTable.AppliesToAnyOf(SeriesOnly, Scope(RuleItemKind.Movie)));

    /// <summary>
    /// The done condition this test carries, second half: both names are in the message.
    /// </summary>
    [Fact]
    public void TheRefusalNamesTheFieldAndWhatTheRuleCollects()
    {
        var refusal = RuleFieldTable.RefuseOutsideScope(
            SeriesOnly,
            Scope(RuleItemKind.Movie),
            "/match/allOf/0/field");

        Assert.Equal("/match/allOf/0/field", refusal.Pointer);
        Assert.Contains("\"seasonCount\"", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("The rule collects movie", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("applies to series", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The near miss, and the one that decides how a rule over two kinds behaves. A rule
    /// collecting films and series with a condition that means something for the series and
    /// nothing for the films narrows the series and leaves the films alone, which is what the
    /// document says. Refusing it would make the repair two rules where the operator wrote one.
    /// </summary>
    [Fact]
    public void AFieldThatMeansSomethingForOneOfSeveralCollectedKindsIsInsideTheScope()
        => Assert.True(
            RuleFieldTable.AppliesToAnyOf(SeriesOnly, Scope(RuleItemKind.Movie, RuleItemKind.Series)));

    /// <summary>
    /// A refusal built for something that is not refused is a caller fault and reads as an answer,
    /// which is the pattern the operator refusal beside it already sets.
    /// </summary>
    [Fact]
    public void ARefusalForAFieldTheScopeAcceptsThrowsRatherThanAnswering()
        => Assert.Throws<ArgumentException>(
            () => RuleFieldTable.RefuseOutsideScope(SeriesOnly, Scope(RuleItemKind.Series), "/match"));

    /// <summary>
    /// Both arguments are read, so neither can be forgotten by a caller that has one of them.
    /// </summary>
    [Fact]
    public void TheScopeAnswerAndTheRefusalBothRefuseANullArgument()
    {
        Assert.Throws<ArgumentNullException>(() => RuleFieldTable.AppliesToAnyOf(null!, Scope(RuleItemKind.Movie)));
        Assert.Throws<ArgumentNullException>(() => RuleFieldTable.AppliesToAnyOf(SeriesOnly, null!));
        Assert.Throws<ArgumentNullException>(() => RuleFieldTable.RefuseOutsideScope(null!, Scope(RuleItemKind.Movie), "/match"));
        Assert.Throws<ArgumentNullException>(() => RuleFieldTable.RefuseOutsideScope(SeriesOnly, null!, "/match"));
    }

    /// <summary>
    /// The stage refuses, rather than the table alone. This is the refusal SITE inside the read,
    /// reached over a real document and a real composition tree with one thing standing in: the
    /// vocabulary the stage resolves a name against.
    /// </summary>
    /// <remarks>
    /// The document is what somebody writes when they think a field applies to what they collect.
    /// Against the real vocabulary it is accepted, which is asserted first so the refusal below
    /// cannot be read as the document being wrong in some other way.
    /// </remarks>
    [Fact]
    public void TheStageRefusesAConditionOnAFieldOutsideTheScope()
    {
        const string Text = """
            {
              "schemaVersion": 1,
              "id": "films-only",
              "name": "Films only",
              "collects": ["movie"],
              "match": {
                "allOf": [
                  { "field": "seasonCount", "operator": "equals", "value": 3 }
                ]
              }
            }
            """;

        using var parsed = JsonDocument.Parse(Text);
        var root = parsed.RootElement;
        var composition = RuleCompositionReader.Read(root.GetProperty("match"), "/match");

        Assert.True(composition.IsAccepted);

        var read = RuleFieldReader.Read(
            root,
            composition.Group!,
            Scope(RuleItemKind.Movie),
            name => string.Equals(name, SeriesOnly.Name, StringComparison.Ordinal) ? SeriesOnly : null);

        Assert.False(read.IsAccepted);
        Assert.Empty(read.Fields);

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0/field", error.Pointer);
        Assert.Contains("\"seasonCount\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("The rule collects movie", error.Message, StringComparison.Ordinal);
        Assert.Contains("applies to series", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same document and the same fixture against a scope the field applies to. Without this
    /// the test above passes on a stage that refuses every condition it reads.
    /// </summary>
    [Fact]
    public void TheStageAcceptsTheSameConditionWhereTheScopeIncludesTheKind()
    {
        const string Text = """
            {
              "schemaVersion": 1,
              "id": "series-only",
              "name": "Series only",
              "collects": ["series"],
              "match": {
                "allOf": [
                  { "field": "seasonCount", "operator": "equals", "value": 3 }
                ]
              }
            }
            """;

        using var parsed = JsonDocument.Parse(Text);
        var root = parsed.RootElement;
        var composition = RuleCompositionReader.Read(root.GetProperty("match"), "/match");

        var read = RuleFieldReader.Read(
            root,
            composition.Group!,
            Scope(RuleItemKind.Series),
            name => string.Equals(name, SeriesOnly.Name, StringComparison.Ordinal) ? SeriesOnly : null);

        Assert.True(read.IsAccepted, string.Join("; ", read.Errors.Select(error => error.ToString())));
        Assert.Equal(SeriesOnly, Assert.Single(read.Fields).Row);
    }

    /// <summary>
    /// The public read binds the vocabulary to the table, so a document naming the fixture field
    /// is refused for the field not existing rather than for a scope. A seam that changed what a
    /// document is read against would be a hook, and this is what says it is not one.
    /// </summary>
    [Fact]
    public void ThePublicReadResolvesNamesAgainstTheDeclaredVocabulary()
    {
        const string Text = """
            {
              "schemaVersion": 1,
              "id": "films-only",
              "name": "Films only",
              "collects": ["movie"],
              "match": {
                "allOf": [
                  { "field": "seasonCount", "operator": "equals", "value": 3 }
                ]
              }
            }
            """;

        using var parsed = JsonDocument.Parse(Text);
        var root = parsed.RootElement;
        var composition = RuleCompositionReader.Read(root.GetProperty("match"), "/match");

        var read = RuleFieldReader.Read(root, composition.Group!, Scope(RuleItemKind.Movie));
        var error = Assert.Single(read.Errors);

        Assert.Contains("There is no field called \"seasonCount\".", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("means nothing", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The seam reads its own argument, so a caller handing it nothing is told rather than
    /// meeting a null reference inside the walk.
    /// </summary>
    [Fact]
    public void TheSeamRefusesANullVocabulary()
    {
        using var parsed = JsonDocument.Parse("{\"match\":{\"allOf\":[]}}");
        var composition = RuleCompositionReader.Read(parsed.RootElement.GetProperty("match"), "/match");

        Assert.Throws<ArgumentNullException>(
            () => RuleFieldReader.Read(parsed.RootElement, composition.Group!, Scope(RuleItemKind.Movie), null!));
    }
}
