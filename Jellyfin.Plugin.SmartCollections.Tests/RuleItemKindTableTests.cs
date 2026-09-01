using System;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The item kind table is the authority for what a rule may collect. What has to hold of it is
/// that every member of the enumeration has exactly one row, that no two rows share a name or a
/// server kind, and that the refusal a document meets lists every legal name.
/// </summary>
public class RuleItemKindTableTests
{
    [Fact]
    public void EveryDeclaredKindHasExactlyOneRow()
    {
        var declared = Enum.GetValues<RuleItemKind>();

        Assert.Equal(declared.Length, RuleItemKindTable.Rows.Count);

        foreach (var kind in declared)
        {
            Assert.Single(RuleItemKindTable.Rows, row => row.Kind == kind);
        }
    }

    /// <summary>
    /// Two rows sharing a name would make one of them unreachable from a document, and the index
    /// the table builds would throw on construction rather than saying so here.
    /// </summary>
    [Fact]
    public void NoTwoRowsShareANameOrAServerKind()
    {
        Assert.Equal(
            RuleItemKindTable.Rows.Count,
            RuleItemKindTable.Rows.Select(row => row.Name).Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(
            RuleItemKindTable.Rows.Count,
            RuleItemKindTable.Rows.Select(row => row.ServerKind).Distinct().Count());
    }

    /// <summary>
    /// A name is a wire token, so it may hold nothing a locale, a shell or a URL would read
    /// differently. The set is the same one the schema's own list is held to.
    /// </summary>
    [Fact]
    public void EveryNameIsLowercaseAsciiLetters()
    {
        foreach (var row in RuleItemKindTable.Rows)
        {
            Assert.NotEmpty(row.Name);
            Assert.All(row.Name, character => Assert.InRange(character, 'a', 'z'));
        }
    }

    [Fact]
    public void EveryRowCarriesASemanticsSentence()
    {
        foreach (var row in RuleItemKindTable.Rows)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Semantics), row.Name + " carries no semantics sentence.");
            Assert.EndsWith(".", row.Semantics, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheNamesAreTheRowsInTheOrderTheTableDeclaresThem()
        => Assert.Equal(RuleItemKindTable.Rows.Select(row => row.Name), RuleItemKindTable.Names);

    [Fact]
    public void EveryDeclaredNameResolvesToItsOwnRow()
    {
        foreach (var row in RuleItemKindTable.Rows)
        {
            Assert.Same(row, RuleItemKindTable.Find(row.Name));
            Assert.Same(row, RuleItemKindTable.Of(row.Kind));
        }
    }

    /// <summary>
    /// The comparison is ordinal, so a server's locale cannot decide whether a document names a
    /// kind. Turkish is the case that makes a culture-sensitive lookup wrong rather than merely
    /// unspecified.
    /// </summary>
    [Theory]
    [InlineData("Movie")]
    [InlineData("MOVIE")]
    [InlineData("mov\u0131e")]
    [InlineData("film")]
    [InlineData("")]
    public void ANameNoKindHasResolvesToNothing(string name)
        => Assert.Null(RuleItemKindTable.Find(name));

    [Fact]
    public void FindRefusesANullName()
        => Assert.Throws<ArgumentNullException>(() => RuleItemKindTable.Find(null!));

    [Fact]
    public void OfRefusesAKindNoRowDeclares()
        => Assert.Throws<ArgumentOutOfRangeException>(() => RuleItemKindTable.Of((RuleItemKind)(-1)));

    /// <summary>
    /// The refusal is what somebody repairing a document reads, so it names what they wrote and
    /// every name they could have written instead.
    /// </summary>
    [Fact]
    public void TheRefusalNamesTheWrittenNameAndEveryLegalOne()
    {
        var error = RuleItemKindTable.RefuseUnknownKind("film", "/collects/0");

        Assert.Equal("/collects/0", error.Pointer);
        Assert.Equal(
            "There is no item kind called \"film\". The kinds a rule may collect are movie, series.",
            error.Message);

        foreach (var name in RuleItemKindTable.Names)
        {
            Assert.Contains(name, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRefusalRefusesANullName()
        => Assert.Throws<ArgumentNullException>(() => RuleItemKindTable.RefuseUnknownKind(null!, "/collects/0"));
}
