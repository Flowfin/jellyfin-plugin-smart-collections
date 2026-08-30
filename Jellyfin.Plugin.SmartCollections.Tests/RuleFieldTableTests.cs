using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The field vocabulary is only closed if every member of it has a row and every row belongs to a
/// member. These tests hold the table to that in both directions, and hold each row to the
/// operator set it borrows from.
/// </summary>
public class RuleFieldTableTests
{
    public static TheoryData<RuleField> EveryField()
    {
        var data = new TheoryData<RuleField>();

        foreach (var field in Enum.GetValues<RuleField>())
        {
            data.Add(field);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryField))]
    public void EveryFieldHasARow(RuleField field)
    {
        Assert.Equal(field, RuleFieldTable.Of(field).Field);
    }

    [Fact]
    public void EveryRowBelongsToAField()
    {
        Assert.Equal(
            Enum.GetValues<RuleField>().OrderBy(field => field).ToArray(),
            RuleFieldTable.Rows.Select(row => row.Field).OrderBy(field => field).ToArray());
    }

    /// <summary>
    /// A field with no row would otherwise reach <c>Of</c> as an unhandled case and be read as
    /// whichever row happened to be first.
    /// </summary>
    [Fact]
    public void AFieldWithNoRowIsRefusedRatherThanResolved()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RuleFieldTable.Of((RuleField)(-1)));
    }

    [Fact]
    public void EveryFieldNameIsWrittenOnceAndTheNamesAreSortedOrdinally()
    {
        var names = RuleFieldTable.Rows.Select(row => row.Name).ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal).ToArray(), RuleFieldTable.Names);
    }

    [Fact]
    public void EveryNameFindsItsOwnRow()
    {
        foreach (var row in RuleFieldTable.Rows)
        {
            Assert.Same(row, RuleFieldTable.Find(row.Name));
        }
    }

    /// <summary>
    /// Ordinal, so a server running in Turkish does not decide whether a document named a field.
    /// </summary>
    [Fact]
    public void ANameThatDiffersOnlyByCaseIsNotThatField()
    {
        Assert.Null(RuleFieldTable.Find("Genres"));
        Assert.Null(RuleFieldTable.Find("GENRES"));
    }

    [Fact]
    public void ANameNoFieldCarriesResolvesToNothing()
    {
        Assert.Null(RuleFieldTable.Find("genre"));
    }

    /// <summary>
    /// Every operator a row declares has to be one the operator set applies to a field of the
    /// type that row holds. Without this the two tables can drift into a field offering a
    /// comparison the operator behind it refuses, which is a rule that validates and then cannot
    /// be compiled.
    /// </summary>
    /// <remarks>
    /// The FIELD end of the operator's row, which is the end this column is comparable with. The
    /// two operators that take no value are inside this check rather than skipped past it: their
    /// field end is every declared type, so asking the question about them is meaningful, and it
    /// was the value end being empty that once made a field declaring one look refusable for a
    /// reason that was not about it.
    /// </remarks>
    [Fact]
    public void EveryOperatorARowDeclaresAppliesToTheTypeThatRowHolds()
    {
        foreach (var row in RuleFieldTable.Rows)
        {
            foreach (var @operator in row.Operators)
            {
                var declared = RuleOperatorTable.Of(@operator);

                Assert.True(
                    RuleOperatorTable.AcceptsField(@operator, row.ValueType),
                    row.Name + " declares " + declared.Name
                    + ", which the operator set does not apply to a field of type " + row.ValueType + ".");
            }
        }
    }

    /// <summary>
    /// Without this the test above passes on a table where every row declared only operators that
    /// take no value, which is the shape a reader would least expect it to be blind to.
    /// </summary>
    [Fact]
    public void TheRowsDeclareOperatorsThatTakeAValue()
    {
        Assert.Contains(
            RuleFieldTable.Rows,
            row => row.Operators.Any(@operator => RuleOperatorTable.Of(@operator).TakesAValue));
    }

    /// <summary>
    /// The defect this vocabulary was born with, refused rather than left for a reader to catch.
    /// An operator in the closed set that no field declares is one no rule anybody writes can
    /// name, so it is documented, tested and unreachable - which is exactly what
    /// <c>withinLast</c> was while the operator table carried one type column read as the field's.
    /// </summary>
    [Fact]
    public void EveryOperatorTheClosedSetDeclaresIsReachableFromAtLeastOneField()
    {
        var declared = RuleFieldTable.Rows
            .SelectMany(row => row.Operators)
            .Distinct()
            .Select(@operator => RuleOperatorTable.Of(@operator).Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(RuleOperatorTable.Names, declared);
    }

    [Fact]
    public void EveryRowDeclaresAtLeastOneOperatorAndDeclaresNoneTwice()
    {
        foreach (var row in RuleFieldTable.Rows)
        {
            Assert.NotEmpty(row.Operators);
            Assert.Equal(row.Operators.Count, row.Operators.Distinct().Count());
        }
    }

    /// <summary>
    /// The enumeration parser is handed the list of names a field accepts, and no row carries a
    /// column for that list. A row declaring the type would reach that parser with nothing to
    /// compare against.
    /// </summary>
    [Fact]
    public void NoRowDeclaresAValueTypeThisTableCannotCarryTheNamesFor()
    {
        Assert.DoesNotContain(RuleValueType.Enumeration, RuleFieldTable.Rows.Select(row => row.ValueType));
    }

    [Fact]
    public void EveryRowSaysWhatItHoldsInOneSentence()
    {
        foreach (var row in RuleFieldTable.Rows)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Semantics), row.Name + " says nothing about what it holds.");
            Assert.EndsWith(".", row.Semantics, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void APostQueryRowIsExactlyOneWithNoQueryProperty()
    {
        foreach (var row in RuleFieldTable.Rows)
        {
            Assert.Equal(row.QueryProperty is null, row.IsPostQuery);
        }
    }

    [Fact]
    public void AFieldAcceptsTheOperatorsItsRowDeclaresAndNoOther()
    {
        Assert.True(RuleFieldTable.Accepts(RuleField.Genres, RuleOperator.Contains));
        Assert.False(RuleFieldTable.Accepts(RuleField.Genres, RuleOperator.StartsWith));
        Assert.False(RuleFieldTable.Accepts(RuleField.Overview, RuleOperator.Equals));
    }

    /// <summary>
    /// The two date fields are where <c>withinLast</c> becomes writable, and a rule naming it on
    /// anything else is refused. Named rather than derived, because the derivation is the test
    /// above and this is the reading that says which fields it landed on.
    /// </summary>
    [Fact]
    public void TheDateFieldsDeclareWithinLastAndNoOtherFieldDoes()
    {
        Assert.True(RuleFieldTable.Accepts(RuleField.DateAdded, RuleOperator.WithinLast));
        Assert.True(RuleFieldTable.Accepts(RuleField.PremiereDate, RuleOperator.WithinLast));

        Assert.Equal(
            new[] { "dateAdded", "premiereDate" },
            RuleFieldTable.Rows
                .Where(row => row.Operators.Contains(RuleOperator.WithinLast))
                .Select(row => row.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void TheRefusalNamesTheNameThatWasWrittenAndEveryLegalOne()
    {
        var error = RuleFieldTable.RefuseUnknownField("genre", "/match/allOf/0/field");

        Assert.Equal("/match/allOf/0/field", error.Pointer);
        Assert.Contains("\"genre\"", error.Message, StringComparison.Ordinal);

        foreach (var name in RuleFieldTable.Names)
        {
            Assert.Contains(name, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRefusalRefusesANullName()
    {
        Assert.Throws<ArgumentNullException>(() => RuleFieldTable.RefuseUnknownField(null!, string.Empty));
    }

    [Fact]
    public void FindRefusesANullName()
    {
        Assert.Throws<ArgumentNullException>(() => RuleFieldTable.Find(null!));
    }

    /// <summary>
    /// The two tables are read together everywhere a condition is judged, so a name colliding
    /// across them is a document whose reader cannot say which table a word came from.
    /// </summary>
    [Fact]
    public void NoFieldIsSpelledLikeAnOperator()
    {
        Assert.Empty(RuleFieldTable.Names.Intersect(RuleOperatorTable.Names, StringComparer.Ordinal));
    }
}
