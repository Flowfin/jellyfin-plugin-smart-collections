using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SmartCollections.Evaluation;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The comparison the order is built out of, at the two places a document cannot reach.
/// </summary>
/// <remarks>
/// What a rule's order DOES is asserted through documents in <see cref="RuleOrderTests"/>. What is
/// here is the pair of arms no document produces: the guards on the arguments, and the shape that
/// has no order. Both exist because a caller may build a term some other way, and a guard with no
/// proof that it bites is refused in this repository.
/// </remarks>
public class ItemOrderTests
{
    /// <summary>
    /// The two arguments that must be there are refused rather than read as nothing.
    /// </summary>
    [Fact]
    public void TheArgumentsThatMustBeThereAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => ItemOrder.Take(null!, [], null));
        Assert.Throws<ArgumentNullException>(() => ItemOrder.Take([], null!, null));
    }

    /// <summary>
    /// The arm no document reaches: a field whose value is a list has no order, so a term naming
    /// one throws here rather than producing an order somebody invented.
    /// </summary>
    /// <remarks>
    /// <see cref="RuleSortTable"/> refuses such a term, and <c>RuleSortTableTests</c> holds that
    /// refusal to the shapes the reader produces, so this arm is unreachable through a document by
    /// construction. The reading is built through the constructor the type opens for exactly this.
    /// </remarks>
    [Fact]
    public void AShapeWithNoOrderThrowsRatherThanBeingOrdered()
    {
        var list = new ItemFieldReading(
            ItemFieldShape.TextList,
            true,
            null,
            ["Drama"],
            0,
            default,
            default);

        Assert.Throws<ArgumentOutOfRangeException>(() => ItemOrder.Compare(list, list, RuleSortDirection.Ascending));
    }

    /// <summary>
    /// Two items that both hold nothing for a field are equal under that term, so the term after
    /// it decides rather than the order they arrived in.
    /// </summary>
    [Fact]
    public void TwoItemsHoldingNothingAreEqualUnderThatTerm()
    {
        var absent = new ItemFieldReading(ItemFieldShape.Number, false, null, [], 0, default, default);

        Assert.Equal(0, ItemOrder.Compare(absent, absent, RuleSortDirection.Descending));
    }

    /// <summary>
    /// An item that holds a value sorts before one that does not, and the answer is the same way
    /// round whichever of the two the comparison is handed first.
    /// </summary>
    /// <param name="direction">The direction the term declares.</param>
    /// <remarks>
    /// Both argument orders are asserted because a sort hands a comparison its pair in whichever
    /// order its own algorithm reached them in, so an absence rule that answered only one way
    /// round would put an item with no value in a different place depending on where the server
    /// happened to list it.
    /// </remarks>
    [Theory]
    [InlineData(RuleSortDirection.Ascending)]
    [InlineData(RuleSortDirection.Descending)]
    public void AnItemHoldingNothingSortsAfterOneThatHoldsAValue(RuleSortDirection direction)
    {
        var absent = new ItemFieldReading(ItemFieldShape.Number, false, null, [], 0, default, default);
        var present = new ItemFieldReading(ItemFieldShape.Number, true, null, [], 7, default, default);

        Assert.True(ItemOrder.Compare(present, absent, direction) < 0);
        Assert.True(ItemOrder.Compare(absent, present, direction) > 0);
    }

    /// <summary>
    /// An order with no terms at all is the identifier order, which is what a document declaring
    /// no sort gets.
    /// </summary>
    [Fact]
    public void AnOrderWithNoTermsIsTheIdentifierOrder()
    {
        var later = new Movie { Id = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var earlier = new Movie { Id = Guid.Parse("11111111-1111-1111-1111-111111111111") };

        var ordered = ItemOrder.Take([later, earlier], [], null);

        Assert.Equal([earlier.Id, later.Id], ordered);
    }

    /// <summary>
    /// Every shape a term may name is ordered by the value it carries rather than by anything
    /// else, in both directions.
    /// </summary>
    /// <param name="field">The field the term names.</param>
    /// <param name="direction">The direction the term declares.</param>
    /// <param name="firstIsSmaller">Whether the first item holds the smaller value.</param>
    [Theory]
    [InlineData(RuleField.CommunityRating, RuleSortDirection.Ascending, true)]
    [InlineData(RuleField.CommunityRating, RuleSortDirection.Descending, true)]
    [InlineData(RuleField.Name, RuleSortDirection.Ascending, true)]
    [InlineData(RuleField.PremiereDate, RuleSortDirection.Ascending, true)]
    [InlineData(RuleField.Runtime, RuleSortDirection.Ascending, true)]
    public void EveryShapeATermMayNameIsOrderedByItsValue(
        RuleField field,
        RuleSortDirection direction,
        bool firstIsSmaller)
    {
        var smaller = new Movie
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CommunityRating = 4.5f,
            Name = "A Quiet Film",
            PremiereDate = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RunTimeTicks = TimeSpan.FromMinutes(88).Ticks
        };
        var larger = new Movie
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CommunityRating = 8.1f,
            Name = "The Job",
            PremiereDate = new DateTime(1994, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            RunTimeTicks = TimeSpan.FromMinutes(101).Ticks
        };

        Assert.True(firstIsSmaller);

        var terms = new[] { new RuleSortTerm(RuleFieldTable.Of(field), direction, "/sort/0") };
        var ordered = ItemOrder.Take([larger, smaller], terms, null);

        var expected = direction == RuleSortDirection.Ascending
            ? new[] { smaller.Id, larger.Id }
            : [larger.Id, smaller.Id];

        Assert.Equal(expected, ordered);
    }

    /// <summary>
    /// The identifiers of the two items above are deliberately the wrong way round for every
    /// assertion, so a comparison that ignored the term and fell straight through to the
    /// tie-break would fail rather than agree by accident.
    /// </summary>
    [Fact]
    public void TheTieBreakDoesNotAgreeWithTheTermsByAccident()
    {
        IReadOnlyList<BaseItem> items =
        [
            new Movie { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "The Job" },
            new Movie { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "A Quiet Film" }
        ];

        var terms = new[]
        {
            new RuleSortTerm(RuleFieldTable.Of(RuleField.Name), RuleSortDirection.Ascending, "/sort/0")
        };

        Assert.Equal([items[1].Id, items[0].Id], ItemOrder.Take(items, terms, null));
    }
}
