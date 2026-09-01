using System;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The scope stage reads what a rule collects. What has to hold of it is that the member is
/// required rather than defaulted, that a name outside the declared list is refused with the list
/// in the message, that a repeat is refused rather than folded away, and that the kinds come back
/// in the table's order however the document wrote them.
/// </summary>
public class RuleItemScopeReaderTests
{
    private static RuleItemScopeRead Read(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        return RuleItemScopeReader.Read(parsed.RootElement);
    }

    private static RuleValidationError Single(string json)
    {
        var read = Read(json);

        Assert.False(read.IsAccepted);
        Assert.Empty(read.Kinds);
        return Assert.Single(read.Errors);
    }

    [Fact]
    public void ADocumentThatNamesOneKindCollectsThatKind()
    {
        var read = Read("{\"collects\": [\"series\"]}");

        Assert.True(read.IsAccepted, "Refused with: " + string.Join(" | ", read.Errors));
        Assert.Equal([RuleItemKind.Series], read.Kinds.Select(row => row.Kind));
    }

    [Fact]
    public void ADocumentMayNameEveryDeclaredKind()
    {
        var read = Read("{\"collects\": [\"movie\", \"series\"]}");

        Assert.True(read.IsAccepted, "Refused with: " + string.Join(" | ", read.Errors));
        Assert.Equal(RuleItemKindTable.Rows, read.Kinds);
    }

    /// <summary>
    /// A scope is a set, so the order a document wrote it in is not carried. Two documents naming
    /// one set in two orders have to produce one scope, or the query a rule compiles to depends on
    /// how somebody typed it.
    /// </summary>
    [Fact]
    public void TheKindsComeBackInTheTablesOrderRatherThanTheDocumentsInEitherDirection()
    {
        var written = Read("{\"collects\": [\"series\", \"movie\"]}");
        var reversed = Read("{\"collects\": [\"movie\", \"series\"]}");

        Assert.True(written.IsAccepted);
        Assert.Equal(RuleItemKindTable.Names, written.Kinds.Select(row => row.Name));
        Assert.Equal(reversed.Kinds, written.Kinds);
    }

    /// <summary>
    /// The done condition this test carries: a document with no scope is refused, and the message
    /// lists the kinds it could have named.
    /// </summary>
    [Fact]
    public void ADocumentWithNoScopeIsRefusedWithTheLegalKindsInTheMessage()
    {
        var error = Single("{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"Christmas\"}");

        Assert.Equal("/collects", error.Pointer);
        Assert.Equal(
            "The document declares no collects. Every rule document says which item kinds it collects, "
            + "as an array of one or more of movie, series. It is refused rather than defaulted, because "
            + "a rule with no scope is a rule that reads every item in the library.",
            error.Message);
    }

    /// <summary>
    /// The stage is handed the top level of a document, and a caller that hands it something else
    /// gets the same refusal as a document with no member rather than an exception. There is no
    /// scope either way, and the message an operator reads should not depend on which.
    /// </summary>
    [Theory]
    [InlineData("[\"movie\"]")]
    [InlineData("\"movie\"")]
    [InlineData("7")]
    [InlineData("null")]
    public void SomethingThatIsNotADocumentIsRefusedTheSameWayAsADocumentWithNoScope(string json)
    {
        var error = Single(json);

        Assert.Equal("/collects", error.Pointer);
        Assert.StartsWith("The document declares no collects.", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A single name is refused rather than read as a list of one. Accepting both spellings would
    /// mean a rule that later collects two kinds changes shape as well as scope, and every reader
    /// of the format would have to accept both forever.
    /// </summary>
    [Theory]
    [InlineData("{\"collects\": \"movie\"}")]
    [InlineData("{\"collects\": {\"movie\": true}}")]
    [InlineData("{\"collects\": 7}")]
    [InlineData("{\"collects\": true}")]
    [InlineData("{\"collects\": false}")]
    [InlineData("{\"collects\": null}")]
    public void AScopeThatIsNotAnArrayIsRefused(string json)
    {
        var error = Single(json);

        Assert.Equal("/collects", error.Pointer);
        Assert.Equal(
            "collects has to be an array naming one or more of movie, series, and this document "
            + "writes something else there. A single name written on its own is refused rather than "
            + "read as a list of one, because a rule that later collects two kinds would then change "
            + "shape as well as scope.",
            error.Message);
    }

    [Fact]
    public void AnEmptyScopeIsRefused()
    {
        var error = Single("{\"collects\": []}");

        Assert.Equal("/collects", error.Pointer);
        Assert.Equal(
            "collects is empty, and a rule that collects no kind of item collects nothing. "
            + "The kinds a rule may collect are movie, series.",
            error.Message);
    }

    [Fact]
    public void AMemberThatIsNotAStringIsRefusedAtItsOwnPosition()
    {
        var error = Single("{\"collects\": [13]}");

        Assert.Equal("/collects/0", error.Pointer);
        Assert.Equal("An item kind is written as a string naming one of movie, series.", error.Message);
    }

    /// <summary>
    /// The done condition's other half of the same clause: a name outside the declared list is
    /// refused with the list, wherever in the array it sits.
    /// </summary>
    [Fact]
    public void ANameNoKindHasIsRefusedAtItsOwnPositionWithTheLegalKinds()
    {
        var error = Single("{\"collects\": [\"movie\", \"episode\"]}");

        Assert.Equal("/collects/1", error.Pointer);
        Assert.Equal(
            "There is no item kind called \"episode\". The kinds a rule may collect are movie, series.",
            error.Message);
    }

    /// <summary>
    /// The comparison is ordinal, so a name spelled with the server's own capital is refused here
    /// rather than accepted on a server whose locale folds it.
    /// </summary>
    [Fact]
    public void ANameSpelledWithACapitalIsRefused()
    {
        var error = Single("{\"collects\": [\"Movie\"]}");

        Assert.Equal("/collects/0", error.Pointer);
        Assert.StartsWith("There is no item kind called \"Movie\".", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARepeatedKindIsRefusedNamingWhereItWasFirstCollected()
    {
        var error = Single("{\"collects\": [\"movie\", \"series\", \"movie\"]}");

        Assert.Equal("/collects/2", error.Pointer);
        Assert.Equal(
            "\"movie\" is already collected, at position 0. A rule names each kind once, and a repeat "
            + "is left to be repaired rather than ignored.",
            error.Message);
    }

    /// <summary>
    /// Every reason rather than the first, for the reason the neighbouring stages give: a list with
    /// two mistakes in it is one repair when both are named and two when they arrive one at a time.
    /// </summary>
    [Fact]
    public void EveryBadMemberIsReportedRatherThanTheFirst()
    {
        var read = Read("{\"collects\": [\"episode\", 13, \"season\"]}");

        Assert.False(read.IsAccepted);
        Assert.Equal(["/collects/0", "/collects/1", "/collects/2"], read.Errors.Select(error => error.Pointer));
    }

    /// <summary>
    /// A document that names one kind well and one badly carries no scope at all. Reading the good
    /// half would run a rule over a scope its author did not write.
    /// </summary>
    [Fact]
    public void OneBadMemberRefusesTheWholeScope()
    {
        var read = Read("{\"collects\": [\"movie\", \"episode\"]}");

        Assert.False(read.IsAccepted);
        Assert.Empty(read.Kinds);
    }
}
