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
    /// A field the field table marks as read after the query has nothing on the server's query to
    /// narrow on, so a row here for one of its operators would be a query built on a property the
    /// field is not about.
    /// </summary>
    [Fact]
    public void EveryRowIsForAFieldTheFieldTableNarrowsOn()
    {
        foreach (var row in RuleQueryTable.Rows)
        {
            Assert.False(
                RuleFieldTable.Of(row.Field).IsPostQuery,
                RuleFieldTable.Of(row.Field).Name + " is read after the query and this table "
                + "compiles a pair over it.");
        }
    }

    /// <summary>
    /// The done condition this test carries: the compiler produces a query for every vocabulary
    /// row that names one. A field the field table says the query narrows on, with no pair here,
    /// is a promise the compiler does not keep.
    /// </summary>
    [Fact]
    public void EveryFieldTheFieldTableNarrowsOnHasAPairThatCompiles()
    {
        foreach (var field in RuleFieldTable.Rows.Where(row => !row.IsPostQuery))
        {
            Assert.True(
                RuleQueryTable.Narrows(field.Field),
                field.Name + " names InternalItemsQuery." + field.QueryProperty
                + " and no pair over it compiles.");
        }
    }

    /// <summary>
    /// A field the field table does not narrow on has no pair here, which is the other direction
    /// of the test above and is what stops it passing on a table that compiles everything.
    /// </summary>
    [Fact]
    public void NoFieldReadAfterTheQueryHasAPairThatCompiles()
    {
        foreach (var field in RuleFieldTable.Rows.Where(row => row.IsPostQuery))
        {
            Assert.False(RuleQueryTable.Narrows(field.Field), field.Name + " is compiled into the query.");
        }
    }

    [Fact]
    public void EveryRowWritesAPropertyTheServerQueryCarries()
    {
        foreach (var row in RuleQueryTable.Rows)
        {
            Assert.True(
                typeof(InternalItemsQuery).GetProperty(row.QueryProperty) is not null,
                row.QueryProperty + " is not on the server query the suite is compiled against.");
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
            .GroupBy(row => row.QueryProperty, StringComparer.Ordinal)
            .Select(group => new { group.Key, Fields = group.Select(row => row.Field).Distinct().ToArray() })
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
        Assert.Equal("Name", row!.QueryProperty);
    }

    [Fact]
    public void AWriteOntoNothingIsRefusedRatherThanIgnored()
    {
        var row = RuleQueryTable.Find(RuleField.Name, RuleOperator.Equals)!;

        Assert.Throws<ArgumentNullException>(
            () => row.TryWrite(null!, [RuleValue.Of(RuleValueType.String, "Heat")]));
        Assert.Throws<ArgumentNullException>(() => row.TryWrite(new InternalItemsQuery(), null!));
    }
}
