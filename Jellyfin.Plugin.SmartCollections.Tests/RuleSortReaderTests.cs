using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The stage that reads the order a rule declares and the cap it may put on the collection.
/// </summary>
/// <remarks>
/// Every refusal this stage can raise is written out here, and each case is a document rather than
/// a constructed argument, because a document is what an operator writes and a pointer into one is
/// what a refusal has to be readable against.
/// </remarks>
public class RuleSortReaderTests
{
    /// <summary>
    /// A field that means something for a series and nothing for a film, which is the only way to
    /// reach the arm that refuses a term naming a field outside what the rule collects. Every
    /// field this version declares applies to both kinds, and <c>RuleFieldScopeTests</c> is where
    /// that sentence is asserted rather than assumed.
    /// </summary>
    private static RuleFieldRow SeriesOnly { get; } = new(
        RuleField.ProductionYear,
        "seasonCount",
        RuleValueType.Integer,
        [RuleOperator.Equals],
        [RuleItemKind.Series],
        null,
        "How many seasons the series has.");

    /// <summary>
    /// A document that declares no order reads as no terms and no cap, rather than as a refusal.
    /// </summary>
    [Fact]
    public void ADocumentThatDeclaresNoOrderIsAccepted()
    {
        var read = Read("{\"collects\":[\"movie\"]}");

        Assert.True(read.IsAccepted);
        Assert.Empty(read.Terms);
        Assert.Null(read.Limit);
    }

    /// <summary>
    /// The terms arrive in the order the document wrote them, which is what makes an order an
    /// order rather than a set.
    /// </summary>
    [Fact]
    public void TheTermsArriveInTheOrderTheDocumentWroteThem()
    {
        var read = Read(
            "{\"collects\":[\"movie\"],\"sort\":["
            + "{\"field\":\"premiereDate\",\"direction\":\"descending\"},"
            + "{\"field\":\"name\",\"direction\":\"ascending\"}]}");

        Assert.True(read.IsAccepted, Because(read));
        Assert.Equal(
            [RuleField.PremiereDate, RuleField.Name],
            read.Terms.Select(term => term.Field.Field).ToArray());
        Assert.Equal(
            [RuleSortDirection.Descending, RuleSortDirection.Ascending],
            read.Terms.Select(term => term.Direction).ToArray());
        Assert.Equal(["/sort/0", "/sort/1"], read.Terms.Select(term => term.Pointer).ToArray());
    }

    /// <summary>
    /// A cap beside an order is read as the number it is.
    /// </summary>
    [Fact]
    public void ACapBesideAnOrderIsRead()
    {
        var read = Read(
            "{\"collects\":[\"movie\"],\"limit\":50,"
            + "\"sort\":[{\"field\":\"name\",\"direction\":\"ascending\"}]}");

        Assert.True(read.IsAccepted, Because(read));
        Assert.Equal(50, read.Limit);
    }

    /// <summary>
    /// The done condition this test carries: a rule with a limit and no sort is refused at
    /// validation.
    /// </summary>
    [Fact]
    public void ACapWithNoOrderIsRefused()
    {
        var read = Read("{\"collects\":[\"movie\"],\"limit\":50}");

        Assert.False(read.IsAccepted);
        var error = Assert.Single(read.Errors);
        Assert.Equal("/limit", error.Pointer);
        Assert.Contains("Declare the order the cap applies to", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A document whose sort is wrong is told what is wrong with the sort, rather than told that
    /// its cap has no order, which is a repair it does not need.
    /// </summary>
    /// <remarks>
    /// The pair refusal reads the terms the sort produced, and a refused sort produces none, so
    /// without this ordering a document with one mistake would be handed two reasons and the
    /// second of them would be wrong.
    /// </remarks>
    [Fact]
    public void ACapBesideASortThatWasRefusedIsNotAlsoRefusedForHavingNoOrder()
    {
        var read = Read("{\"collects\":[\"movie\"],\"limit\":50,\"sort\":\"premiereDate\"}");

        Assert.False(read.IsAccepted);
        Assert.Equal("/sort", Assert.Single(read.Errors).Pointer);
    }

    /// <summary>
    /// Every shape the sort member can be written in that this stage refuses, and where each
    /// refusal points.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="pointer">Where the refusal points.</param>
    /// <param name="fragment">A phrase the message carries, so the case is about this refusal.</param>
    [Theory]
    [InlineData("{\"collects\":[\"movie\"],\"sort\":\"premiereDate\"}", "/sort", "has to be an array of terms")]
    [InlineData("{\"collects\":[\"movie\"],\"sort\":[]}", "/sort", "is empty")]
    [InlineData("{\"collects\":[\"movie\"],\"sort\":[\"premiereDate\"]}", "/sort/0", "A sort term is an object")]
    [InlineData("{\"collects\":[\"movie\"],\"sort\":[{\"direction\":\"ascending\"}]}", "/sort/0/field", "names the field it orders by")]
    [InlineData("{\"collects\":[\"movie\"],\"sort\":[{\"field\":1,\"direction\":\"ascending\"}]}", "/sort/0/field", "written as a string")]
    [InlineData("{\"collects\":[\"movie\"],\"sort\":[{\"field\":\"seasons\",\"direction\":\"ascending\"}]}", "/sort/0/field", "There is no field called")]
    [InlineData("{\"collects\":[\"movie\"],\"sort\":[{\"field\":\"genres\",\"direction\":\"ascending\"}]}", "/sort/0/field", "cannot be ordered by")]
    [InlineData("{\"collects\":[\"movie\"],\"sort\":[{\"field\":\"name\"}]}", "/sort/0/direction", "names the direction it orders in")]
    [InlineData("{\"collects\":[\"movie\"],\"sort\":[{\"field\":\"name\",\"direction\":2}]}", "/sort/0/direction", "written as a string")]
    [InlineData("{\"collects\":[\"movie\"],\"sort\":[{\"field\":\"name\",\"direction\":\"upwards\"}]}", "/sort/0/direction", "There is no sort direction called")]
    [InlineData("{\"collects\":[\"movie\"],\"limit\":\"50\",\"sort\":[{\"field\":\"name\",\"direction\":\"ascending\"}]}", "/limit", "whole number of items")]
    [InlineData("{\"collects\":[\"movie\"],\"limit\":1.5,\"sort\":[{\"field\":\"name\",\"direction\":\"ascending\"}]}", "/limit", "whole number of items")]
    [InlineData("{\"collects\":[\"movie\"],\"limit\":0,\"sort\":[{\"field\":\"name\",\"direction\":\"ascending\"}]}", "/limit", "collects at least one item")]
    public void EachRefusalNamesWhereItIsAndWhatIsWrong(string document, string pointer, string fragment)
    {
        var read = Read(document);

        Assert.False(read.IsAccepted);
        var error = Assert.Single(read.Errors);
        Assert.Equal(pointer, error.Pointer);
        Assert.Contains(fragment, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A second term over one field is refused rather than folded away, and the refusal says where
    /// the first one is.
    /// </summary>
    [Fact]
    public void ASecondTermOverOneFieldIsRefusedAndNamesTheFirst()
    {
        var read = Read(
            "{\"collects\":[\"movie\"],\"sort\":["
            + "{\"field\":\"name\",\"direction\":\"ascending\"},"
            + "{\"field\":\"name\",\"direction\":\"descending\"}]}");

        Assert.False(read.IsAccepted);
        var error = Assert.Single(read.Errors);
        Assert.Equal("/sort/1", error.Pointer);
        Assert.Contains("at position 0", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every reason is collected rather than the first, so a sort with two mistakes in it is one
    /// repair.
    /// </summary>
    [Fact]
    public void TwoMistakesInOneOrderAreBothNamed()
    {
        var read = Read(
            "{\"collects\":[\"movie\"],\"sort\":["
            + "{\"field\":\"tags\",\"direction\":\"ascending\"},"
            + "{\"field\":\"name\",\"direction\":\"sideways\"}]}");

        Assert.False(read.IsAccepted);
        Assert.Equal(["/sort/0/field", "/sort/1/direction"], read.Errors.Select(error => error.Pointer).ToArray());
    }

    /// <summary>
    /// A term whose field is wrong and whose direction is wrong is two reasons rather than one,
    /// because both are read before either is answered.
    /// </summary>
    [Fact]
    public void OneTermWrongInBothMembersIsTwoReasons()
    {
        var read = Read("{\"collects\":[\"movie\"],\"sort\":[{\"field\":\"tags\",\"direction\":\"sideways\"}]}");

        Assert.False(read.IsAccepted);
        Assert.Equal(["/sort/0/field", "/sort/0/direction"], read.Errors.Select(error => error.Pointer).ToArray());
    }

    /// <summary>
    /// A term naming a field that means nothing for anything the rule collects is refused, and the
    /// fixture vocabulary is the only way to reach it.
    /// </summary>
    /// <remarks>
    /// The seam this uses is the one the field stage carries for the same arm and for the same
    /// reason. On the day a declared field is narrowed to fewer kinds this becomes reachable
    /// through a document, and the fixture stays because it is still the cheaper proof.
    /// </remarks>
    [Fact]
    public void ATermNamingAFieldOutsideWhatTheRuleCollectsIsRefused()
    {
        using var parsed = JsonDocument.Parse(
            "{\"collects\":[\"movie\"],\"sort\":[{\"field\":\"seasonCount\",\"direction\":\"ascending\"}]}");
        var root = parsed.RootElement;

        var read = RuleSortReader.Read(
            root,
            RuleItemScopeReader.Read(root).Kinds,
            name => string.Equals(name, SeriesOnly.Name, StringComparison.Ordinal) ? SeriesOnly : null);

        Assert.False(read.IsAccepted);
        var error = Assert.Single(read.Errors);
        Assert.Equal("/sort/0/field", error.Pointer);
        Assert.Contains("seasonCount", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The seam refuses what it is handed nothing for, so a caller that forgot an argument meets
    /// an exception rather than a read over an empty vocabulary.
    /// </summary>
    [Fact]
    public void TheSeamRefusesAScopeOrAVocabularyThatIsNotThere()
    {
        using var parsed = JsonDocument.Parse("{\"collects\":[\"movie\"]}");
        var root = parsed.RootElement;

        Assert.Throws<ArgumentNullException>(() => RuleSortReader.Read(root, null!, RuleFieldTable.Find));
        Assert.Throws<ArgumentNullException>(
            () => RuleSortReader.Read(root, RuleItemScopeReader.Read(root).Kinds, null!));
    }

    /// <summary>
    /// A document that is not an object at all reads as no order rather than throwing, which is
    /// what the stages beside this one do with the same input.
    /// </summary>
    [Fact]
    public void ADocumentThatIsNotAnObjectReadsAsNoOrder()
    {
        using var parsed = JsonDocument.Parse("[]");

        var read = RuleSortReader.Read(parsed.RootElement, []);

        Assert.True(read.IsAccepted);
        Assert.Empty(read.Terms);
        Assert.Null(read.Limit);
    }

    private static RuleSortRead Read(string document)
    {
        using var parsed = JsonDocument.Parse(document);
        var root = parsed.RootElement;

        return RuleSortReader.Read(root, RuleItemScopeReader.Read(root).Kinds);
    }

    private static string Because(RuleSortRead read)
        => string.Join("; ", read.Errors.Select(error => error.Pointer + ": " + error.Message));
}
