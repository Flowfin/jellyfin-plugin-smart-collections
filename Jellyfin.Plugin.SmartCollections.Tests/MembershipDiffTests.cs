using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Membership;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// What a refresh is allowed to do to a collection.
/// </summary>
/// <remarks>
/// The cost these tests protect is not correctness in the abstract. A diff that reports more work
/// than there is turns a refresh that changes nothing into a write per item, and a diff that
/// depends on the order a query returned its rows makes two servers with the same library disagree
/// about what changed. Both are invisible in a single hand-written example, which is why the
/// property below is checked over generated pairs rather than over one.
/// </remarks>
public class MembershipDiffTests
{
    /// <summary>
    /// How many generated pairs the property is checked over. Every one of them is derived from
    /// its own seed, so a failure names an input somebody can reproduce rather than "one of the
    /// random ones".
    /// </summary>
    private const int GeneratedPairs = 200;

    /// <summary>
    /// The identifiers both sides are drawn from. Small on purpose: a pool the size of the sets
    /// would make an overlap rare, and the interesting cases are the ones where the two
    /// memberships share items.
    /// </summary>
    private const int PoolSize = 12;

    [Fact]
    public void AnItemTheRuleMatchesAndTheCollectionDoesNotHoldIsAdded()
    {
        var held = Identifier(1);
        var wanted = Identifier(2);

        var diff = MembershipDiff.Between([held], [held, wanted]);

        Assert.Equal([wanted], diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Equal([held], diff.Unchanged);
    }

    [Fact]
    public void AnItemTheCollectionHoldsAndTheRuleNoLongerMatchesIsRemoved()
    {
        var stays = Identifier(1);
        var goes = Identifier(2);

        var diff = MembershipDiff.Between([stays, goes], [stays]);

        Assert.Empty(diff.Added);
        Assert.Equal([goes], diff.Removed);
        Assert.Equal([stays], diff.Unchanged);
    }

    /// <summary>
    /// The whole reason the diff exists. A collection whose membership has not moved must produce
    /// no work at all, however many items it holds, because each item in a work list is a write to
    /// the repository and a queued metadata refresh.
    /// </summary>
    [Fact]
    public void AnUnchangedCollectionProducesNoWork()
    {
        var membership = Enumerable.Range(0, PoolSize).Select(Identifier).ToList();

        var diff = MembershipDiff.Between(membership, membership);

        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.True(diff.IsEmpty);
        Assert.Equal(membership.Count, diff.Unchanged.Count);
    }

    [Fact]
    public void ACollectionThatMatchesNothingAndHoldsNothingProducesNoWork()
    {
        var diff = MembershipDiff.Between([], []);

        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Unchanged);
        Assert.True(diff.IsEmpty);
    }

    /// <summary>
    /// A collection holds an item or it does not, so an identifier a caller listed twice is one
    /// item. Without this the add list would carry the same item twice and the refresh would write
    /// it twice.
    /// </summary>
    [Fact]
    public void AnIdentifierRepeatedInEitherInputIsOneItem()
    {
        var repeated = Identifier(1);

        var diff = MembershipDiff.Between([repeated, repeated], [repeated, repeated, Identifier(2)]);

        Assert.Equal([Identifier(2)], diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Equal([repeated], diff.Unchanged);
    }

    [Fact]
    public void ADiffOverNothingIsRefusedRatherThanTreatedAsEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => MembershipDiff.Between(null!, []));
        Assert.Throws<ArgumentNullException>(() => MembershipDiff.Between([], null!));
    }

    /// <summary>
    /// The property, over generated pairs: what is added and what is removed is exactly the
    /// symmetric difference of the two memberships, what stays is exactly their intersection, and
    /// the three lists together account for every identifier either side named, once each.
    /// </summary>
    [Fact]
    public void TheDiffIsExactlyTheSymmetricDifference()
    {
        for (var seed = 1; seed <= GeneratedPairs; seed++)
        {
            var random = new Random(seed);
            var current = Subset(random);
            var target = Subset(random);
            var where = Where(seed, current, target);

            var diff = MembershipDiff.Between(current, target);

            var held = new HashSet<Guid>(current);
            var wanted = new HashSet<Guid>(target);

            var symmetric = new HashSet<Guid>(held);
            symmetric.SymmetricExceptWith(wanted);
            Assert.True(
                symmetric.SetEquals(diff.Added.Concat(diff.Removed)),
                "The add and remove lists are not the symmetric difference. " + where);

            Assert.True(
                wanted.Except(held).ToHashSet().SetEquals(diff.Added),
                "The add list is not the target minus the current membership. " + where);
            Assert.True(
                held.Except(wanted).ToHashSet().SetEquals(diff.Removed),
                "The remove list is not the current membership minus the target. " + where);
            Assert.True(
                held.Intersect(wanted).ToHashSet().SetEquals(diff.Unchanged),
                "The unchanged list is not the intersection. " + where);

            var everything = diff.Added.Concat(diff.Removed).Concat(diff.Unchanged).ToList();
            Assert.True(
                everything.Count == everything.Distinct().Count(),
                "An identifier appears in more than one list, or twice in one. " + where);
            Assert.True(
                held.Union(wanted).ToHashSet().SetEquals(everything),
                "The three lists do not account for every identifier either side named. " + where);

            Assert.True(
                diff.IsEmpty == held.SetEquals(wanted),
                "The diff reports work for an unchanged membership, or none for a changed one. " + where);
        }
    }

    /// <summary>
    /// The order the two memberships arrived in is the order a query returned its rows, which is
    /// not a property of the library. A diff that reads it makes two servers with the same library
    /// disagree about what changed, and makes a checked-in expected file worth nothing.
    /// </summary>
    [Fact]
    public void TheDiffDoesNotDependOnTheOrderEitherSideArrivedIn()
    {
        for (var seed = 1; seed <= GeneratedPairs; seed++)
        {
            var random = new Random(seed);
            var current = Subset(random);
            var target = Subset(random);
            var where = Where(seed, current, target);

            var asGiven = MembershipDiff.Between(current, target);
            var shuffled = MembershipDiff.Between(Shuffled(current, random), Shuffled(target, random));

            Assert.True(asGiven.Added.SequenceEqual(shuffled.Added), "The add list moved. " + where);
            Assert.True(asGiven.Removed.SequenceEqual(shuffled.Removed), "The remove list moved. " + where);
            Assert.True(
                asGiven.Unchanged.SequenceEqual(shuffled.Unchanged),
                "The unchanged list moved. " + where);
        }
    }

    /// <summary>
    /// Ascending order is what makes two diffs comparable at all. Sorting is asserted separately
    /// from the shuffle test because two runs can agree with each other and still both be in
    /// whatever order a hash set happened to enumerate in.
    /// </summary>
    [Fact]
    public void EveryListIsInAscendingOrder()
    {
        for (var seed = 1; seed <= GeneratedPairs; seed++)
        {
            var random = new Random(seed);
            var current = Subset(random);
            var target = Subset(random);
            var where = Where(seed, current, target);

            var diff = MembershipDiff.Between(current, target);

            Assert.True(IsAscending(diff.Added), "The add list is not in ascending order. " + where);
            Assert.True(IsAscending(diff.Removed), "The remove list is not in ascending order. " + where);
            Assert.True(
                IsAscending(diff.Unchanged),
                "The unchanged list is not in ascending order. " + where);
        }
    }

    /// <summary>
    /// One identifier per index, derived rather than generated, so a failing case can be written
    /// out by hand. The bytes are spread across the value rather than left in the first field, so
    /// ascending order is not the order the pool was built in and a sort that did nothing would
    /// still have to be caught.
    /// </summary>
    private static Guid Identifier(int index)
        => new(
            0x5C011EC7,
            (short)(index * 7),
            (short)(index * 3),
            [(byte)(index * 11), (byte)index, 0x5D, 0x1F, 0xF0, 0x00, 0x00, (byte)(index * 5)]);

    private static List<Guid> Subset(Random random)
    {
        var chosen = new List<Guid>();
        for (var index = 0; index < PoolSize; index++)
        {
            if (random.Next(2) == 1)
            {
                chosen.Add(Identifier(index));
            }
        }

        return Shuffled(chosen, random);
    }

    private static List<Guid> Shuffled(IEnumerable<Guid> identifiers, Random random)
    {
        var shuffled = identifiers.ToList();
        for (var index = shuffled.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (shuffled[index], shuffled[swap]) = (shuffled[swap], shuffled[index]);
        }

        return shuffled;
    }

    private static bool IsAscending(IReadOnlyList<Guid> identifiers)
    {
        for (var index = 1; index < identifiers.Count; index++)
        {
            if (identifiers[index - 1].CompareTo(identifiers[index]) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// What a failing case needs to be reproduced: the seed, and the two inputs as they were
    /// handed over. Without the seed a failure names no input at all and the next run generates a
    /// different one.
    /// </summary>
    private static string Where(int seed, IReadOnlyList<Guid> current, IReadOnlyList<Guid> target)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"Seed {seed}. Current: [{string.Join(", ", current)}]. Target: [{string.Join(", ", target)}].");
}
