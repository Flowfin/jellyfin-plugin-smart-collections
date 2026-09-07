using System;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The compile table is a claim about two things the table itself cannot see: that every pair it
/// names is a pair a document may write at all, and that the property each pair writes is one the
/// server's query carries. These tests hold it to both.
/// </summary>
public class RuleQueryTableTests
{
    [Fact]
    public void EveryRowNamesAPairTheFieldTableAllows()
    {
        foreach (var row in RuleQueryTable.Rows)
        {
            Assert.True(
                RuleFieldTable.Accepts(row.Field, row.Operator),
                RuleFieldTable.Of(row.Field).Name + " does not accept "
                + RuleOperatorTable.Of(row.Operator).Name + ", so compiling that pair compiles a "
                + "condition the validator refuses.");
        }
    }

    /// <summary>
    /// The mark is the presence of a row, so there is nothing for it to disagree with. Three tests
    /// stood here holding it against a column on the field table, in both directions, and #31 took
    /// that column away on 2026-09-04. What is left to assert is that the answer and the row are
    /// one thing rather than two that agree.
    /// </summary>
    [Fact]
    public void TheMarkIsExactlyThePresenceOfARow()
    {
        foreach (var field in RuleFieldTable.Rows)
        {
            foreach (var @operator in field.Operators)
            {
                Assert.Equal(
                    RuleQueryTable.Find(field.Field, @operator) is not null,
                    RuleQueryTable.AnswersInTheQuery(field.Field, @operator));
            }
        }
    }

    /// <summary>
    /// THE PROPERTY THE MOVE WAS FOR, and the one a field-level column could not express: a field
    /// the server answers under one operator and not under another. This asserts that such a field
    /// exists rather than describing one, because a vocabulary in which no field split would make
    /// every test around it pass for the wrong reason.
    /// </summary>
    /// <remarks>
    /// It is not a corner of this vocabulary. Most fields the query narrows on at all are split
    /// today, which is the size of what the old column was saying wrongly: it marked such a field
    /// as narrowed by the query while several ways of asking about it were answered after the
    /// query. A field answered under EVERY one of its operators is the other case and exists too,
    /// so the assertion below is a subset relation rather than an equality.
    /// </remarks>
    [Fact]
    public void AFieldIsAnsweredUnderSomeOfItsOperatorsAndNotOthers()
    {
        var split = RuleFieldTable.Rows
            .Where(field => field.Operators.Any(@operator => RuleQueryTable.AnswersInTheQuery(field.Field, @operator))
                && field.Operators.Any(@operator => !RuleQueryTable.AnswersInTheQuery(field.Field, @operator)))
            .ToArray();

        Assert.NotEmpty(split);
        Assert.All(split, field => Assert.True(RuleQueryTable.Narrows(field.Field)));
    }

    /// <summary>
    /// The operators a field is answered under are the field's own, in the field's own order, so a
    /// page or a form reads them beside the operator list they belong to. Deriving them from this
    /// table's order instead would move the answer when a row is added in a different place.
    /// </summary>
    [Fact]
    public void TheOperatorsAFieldIsAnsweredUnderAreItsOwnInItsOwnOrder()
    {
        foreach (var field in RuleFieldTable.Rows)
        {
            var answered = RuleQueryTable.OperatorsAnswered(field.Field);

            Assert.Equal(
                field.Operators.Where(@operator => RuleQueryTable.AnswersInTheQuery(field.Field, @operator)),
                answered);
            Assert.Equal(RuleQueryTable.Narrows(field.Field), answered.Count > 0);
        }
    }

    [Fact]
    public void EveryRowWritesPropertiesTheServerQueryCarries()
    {
        foreach (var row in RuleQueryTable.Rows)
        {
            Assert.NotEmpty(row.QueryProperties);
            foreach (var property in row.QueryProperties)
            {
                Assert.True(
                    typeof(InternalItemsQuery).GetProperty(property) is not null,
                    property + " is not on the server query the suite is compiled against.");
            }
        }
    }

    /// <summary>
    /// A row naming one property twice would claim it twice and refuse itself, which nothing
    /// could write a document to reach.
    /// </summary>
    [Fact]
    public void NoRowNamesOnePropertyTwice()
    {
        foreach (var row in RuleQueryTable.Rows)
        {
            Assert.Equal(row.QueryProperties.Distinct(StringComparer.Ordinal), row.QueryProperties);
        }
    }

    /// <summary>
    /// The compiler refuses a second condition writing a property another condition wrote, and it
    /// names one field in that refusal. That message is only true while a property belongs to one
    /// field, which is a property of this table rather than of the compiler.
    /// </summary>
    [Fact]
    public void NoPropertyIsWrittenByTwoDifferentFields()
    {
        var owners = RuleQueryTable.Rows
            .SelectMany(row => row.QueryProperties.Select(property => (Property: property, row.Field)))
            .GroupBy(entry => entry.Property, StringComparer.Ordinal)
            .Select(group => new { group.Key, Fields = group.Select(entry => entry.Field).Distinct().ToArray() })
            .Where(entry => entry.Fields.Length > 1)
            .Select(entry => entry.Key)
            .ToArray();

        Assert.Empty(owners);
    }

    [Fact]
    public void APairTheTableDoesNotCompileHasNoRow()
    {
        Assert.Null(RuleQueryTable.Find(RuleField.Name, RuleOperator.EndsWith));
    }

    [Fact]
    public void APairTheTableCompilesHasItsRow()
    {
        var row = RuleQueryTable.Find(RuleField.Name, RuleOperator.Equals);

        Assert.NotNull(row);
        Assert.Equal(["Name"], row!.QueryProperties);
    }

    [Fact]
    public void AWriteOntoNothingIsRefusedRatherThanIgnored()
    {
        var row = RuleQueryTable.Find(RuleField.Name, RuleOperator.Equals)!;

        Assert.Throws<ArgumentNullException>(
            () => row.TryWrite(null!, [RuleValue.Of(RuleValueType.String, "Heat")], DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentNullException>(() => row.TryWrite(new InternalItemsQuery(), null!, DateTimeOffset.UnixEpoch));
    }
}
