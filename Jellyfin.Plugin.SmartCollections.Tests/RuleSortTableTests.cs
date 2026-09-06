using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Evaluation;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The sort vocabulary: which fields a rule may order by, and the two directions a term writes.
/// </summary>
/// <remarks>
/// The first test here is the one the table is built around. The table declares the fields it
/// CANNOT order by and takes everything else, so a field added to the vocabulary is sortable
/// unless somebody writes a line - and that default is wrong for a field whose value is a list.
/// What catches it is the comparison below against the shape the reader produces, which is the
/// only place in this tree that knows a field's value is a list.
/// </remarks>
public class RuleSortTableTests
{
    /// <summary>
    /// The done condition this test carries: the fields the table refuses to order by are exactly
    /// the fields whose value the reader answers as a list, in both directions.
    /// </summary>
    /// <remarks>
    /// The item carries a value for every field, so each reading has the shape its field always
    /// has rather than the shape an absent value would give it.
    /// </remarks>
    [Fact]
    public void TheFieldsWithNoOrderAreExactlyTheFieldsWhoseValueIsAList()
    {
        var item = new Movie
        {
            CommunityRating = 8.1f,
            DateCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Genres = ["Drama"],
            Name = "The Job",
            OfficialRating = "PG-13",
            Overview = "A heist in three acts.",
            PremiereDate = new DateTime(1994, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            ProductionYear = 1994,
            RunTimeTicks = TimeSpan.FromMinutes(101).Ticks,
            Tags = ["heist"]
        };

        foreach (var row in RuleFieldTable.Rows)
        {
            var isList = ItemFieldReader.Read(item, row.Field).Shape == ItemFieldShape.TextList;

            Assert.True(
                isList != RuleSortTable.CanSortBy(row.Field),
                isList
                    ? "The value of " + row.Name + " is a list and the table orders by it."
                    : "The value of " + row.Name + " is one value and the table refuses to order by it.");
        }
    }

    /// <summary>
    /// The two halves of the table are the whole vocabulary and nothing twice, so a field cannot
    /// fall out of both.
    /// </summary>
    [Fact]
    public void TheSortableAndUnsortableFieldsAreTheWholeVocabulary()
    {
        Assert.Equal(
            RuleFieldTable.Rows.Select(row => row.Name).ToArray(),
            RuleSortTable.Fields.Concat(RuleSortTable.UnsortableFields)
                .Select(row => row.Name)
                .OrderBy(name => Array.FindIndex(RuleFieldTable.Rows.ToArray(), row => row.Name == name))
                .ToArray());

        Assert.NotEmpty(RuleSortTable.Fields);
        Assert.NotEmpty(RuleSortTable.UnsortableFields);
        Assert.Empty(RuleSortTable.Fields.Intersect(RuleSortTable.UnsortableFields));
    }

    /// <summary>
    /// Every direction the vocabulary declares is written under a name, and that name reads back
    /// as the direction it was written for.
    /// </summary>
    [Fact]
    public void EveryDirectionRoundTripsThroughItsName()
    {
        foreach (var direction in Enum.GetValues<RuleSortDirection>())
        {
            var name = RuleSortTable.NameOf(direction);

            Assert.Equal(direction, RuleSortTable.Find(name));
            Assert.Contains(name, RuleSortTable.WrittenDirections, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A name no direction is written under reads as nothing rather than as the nearest one.
    /// </summary>
    /// <param name="name">The name.</param>
    [Theory]
    [InlineData("Ascending")]
    [InlineData("ASC")]
    [InlineData("upwards")]
    [InlineData("")]
    public void ANameNoDirectionIsWrittenUnderReadsAsNothing(string name)
        => Assert.Null(RuleSortTable.Find(name));

    /// <summary>
    /// The two members that take one refuse a null rather than reading one.
    /// </summary>
    [Fact]
    public void TheMembersThatTakeOneRefuseANull()
    {
        Assert.Throws<ArgumentNullException>(() => RuleSortTable.Find(null!));
        Assert.Throws<ArgumentNullException>(
            () => RuleSortTable.RefuseUnsortableField(null!, "/sort/0/field"));
    }

    /// <summary>
    /// A direction with no name throws rather than answering with one, which is what a member
    /// added to the enumeration without a row here would meet.
    /// </summary>
    [Fact]
    public void ADirectionWithNoNameThrows()
        => Assert.Throws<ArgumentOutOfRangeException>(() => RuleSortTable.NameOf((RuleSortDirection)9999));

    /// <summary>
    /// The refusal for a field whose value is a list names the field and the fields that do have
    /// an order, so the repair is in the message.
    /// </summary>
    [Fact]
    public void TheRefusalForAListNamesTheFieldAndWhatCanBeOrdered()
    {
        var error = RuleSortTable.RefuseUnsortableField(RuleFieldTable.Of(RuleField.Genres), "/sort/0/field");

        Assert.Equal("/sort/0/field", error.Pointer);
        Assert.Contains("genres", error.Message, StringComparison.Ordinal);
        Assert.Contains("premiereDate", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tags,", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The names a term writes are the ones the reader reads, so a message naming a member and the
    /// member a document has to write cannot drift apart.
    /// </summary>
    [Fact]
    public void TheTermMembersAreTheOnesADocumentWrites()
    {
        Assert.Equal("field", RuleSortTable.FieldMember);
        Assert.Equal("direction", RuleSortTable.DirectionMember);
        Assert.Equal("sort", RuleSortReader.SortMember);
        Assert.Equal("limit", RuleSortReader.LimitMember);
    }

    /// <summary>
    /// The written list of sortable names is the table's own order, so a refusal and the page
    /// carry one order rather than two.
    /// </summary>
    [Fact]
    public void TheWrittenFieldNamesAreTheTablesOrder()
        => Assert.Equal(
            string.Join(", ", RuleSortTable.Fields.Select(row => row.Name)),
            RuleSortTable.WrittenFieldNames);
}
