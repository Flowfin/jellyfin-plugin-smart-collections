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
    /// Every operator a row declares that takes a value has to be one the operator set accepts
    /// for the type that row holds. Without this the two tables can drift into a field offering a
    /// comparison the operator behind it refuses, which is a rule that validates and then cannot
    /// be compiled.
    /// </summary>
    /// <remarks>
    /// The two operators that take no value are outside this, and that is the operator set's own
    /// answer rather than a hole cut for them: <c>Accepts</c> is asked whether an operator can
    /// compare a value of a type, <c>isEmpty</c> and <c>isNotEmpty</c> compare no value at all,
    /// and their rows declare the empty set to say so. Asking them the question returns false for
    /// every type, so a field declaring one would be refused for a reason that is not about it.
    /// </remarks>
    [Fact]
    public void EveryOperatorARowDeclaresThatTakesAValueIsAcceptedForTheTypeThatRowHolds()
    {
        foreach (var row in RuleFieldTable.Rows)
        {
            foreach (var @operator in row.Operators)
            {
                var declared = RuleOperatorTable.Of(@operator);

                if (!declared.TakesAValue)
                {
                    continue;
                }

                Assert.True(
                    RuleOperatorTable.Accepts(@operator, row.ValueType),
                    row.Name + " declares " + declared.Name
                    + ", which the operator set does not accept for " + row.ValueType + ".");
            }
        }
    }

    /// <summary>
    /// Without this the test above passes on a table where every row declared only the two
    /// operators it skips, which is the shape a reader would least expect it to be blind to.
    /// </summary>
    [Fact]
    public void TheRowsDeclareOperatorsThatTakeAValue()
    {
        Assert.Contains(
            RuleFieldTable.Rows,
            row => row.Operators.Any(@operator => RuleOperatorTable.Of(@operator).TakesAValue));
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
