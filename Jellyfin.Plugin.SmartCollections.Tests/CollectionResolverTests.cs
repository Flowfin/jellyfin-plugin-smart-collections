using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SmartCollections.Membership;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// Which collection a rule owns, and what a resolve is not allowed to touch on the way to it.
/// </summary>
/// <remarks>
/// Two mistakes are what these tests are about, and both are silent on the server they happen on.
///
/// Adopting by name takes a collection an operator built by hand, with items they chose, and hands
/// it to a rule that will then remove everything the rule does not match. Nothing reports it: the
/// collection is still there, still called what it was called, holding a different set of items.
///
/// Creating instead of adopting fills a library with copies, one per run, and each copy looks
/// exactly like the thing the rule was supposed to keep up to date.
///
/// The fake below is the server as far as this question reaches it: collections carrying a name and
/// a provider dictionary, and a lookup that matches the way the server's own query does, on a
/// provider key and its value together. That is what makes these tests about the resolve rather
/// than about a stub written to agree with it - a resolve that looked collections up by name would
/// find nothing here, because this fake matches on nothing else.
/// </remarks>
public class CollectionResolverTests
{
    private const string RuleId = "nineties-thrillers";
    private const string CollectionName = "Nineties Thrillers";

    /// <summary>
    /// The adopt. A rule that already has a collection keeps it, which is what makes the second
    /// refresh an update rather than a duplicate.
    /// </summary>
    [Fact]
    public async Task ARuleWhoseMarkedCollectionExistsResolvesToItRatherThanCreatingASecond()
    {
        var ownership = new FakeCollectionOwnership();
        var existing = ownership.Put(CollectionName, (CollectionStamp.PluginKey, RuleId));

        var resolved = await new CollectionResolver(ownership).ResolveAsync(Rule(), CancellationToken.None);

        Assert.Equal(existing, resolved);
        Assert.Empty(ownership.Created);
        Assert.Single(ownership.Collections);
    }

    /// <summary>
    /// The create, and the mark it has to carry. A created collection with no mark is one the next
    /// run cannot recognise, so the run after that creates another.
    /// </summary>
    [Fact]
    public async Task ARuleWithNoMarkedCollectionGetsOneCarryingThePluginKeyAndTheRuleId()
    {
        var ownership = new FakeCollectionOwnership();

        var resolved = await new CollectionResolver(ownership).ResolveAsync(Rule(), CancellationToken.None);

        var created = Assert.Single(ownership.Created);
        Assert.Equal(CollectionName, created.Name);
        Assert.Equal(
            new KeyValuePair<string, string>(CollectionStamp.PluginKey, RuleId),
            Assert.Single(created.ProviderIds));
        Assert.Equal(created.Id, resolved);
    }

    /// <summary>
    /// The collection somebody made themselves. It shares the name and carries no mark, and the
    /// rule gets its own rather than taking it over.
    /// </summary>
    [Fact]
    public async Task AnUnmarkedCollectionWithTheSameNameIsNeitherAdoptedNorWrittenTo()
    {
        var ownership = new FakeCollectionOwnership();
        var byHand = ownership.Put(CollectionName);

        var resolved = await new CollectionResolver(ownership).ResolveAsync(Rule(), CancellationToken.None);

        Assert.NotEqual(byHand, resolved);
        Assert.Equal(CollectionName, ownership.NameOf(byHand));
        Assert.Empty(ownership.ProviderIdsOf(byHand));
    }

    /// <summary>
    /// Another plugin's mark on a collection of the same name. The value matches this rule's id and
    /// the key does not, which is the case a lookup on the value alone would get wrong.
    /// </summary>
    [Fact]
    public async Task ACollectionCarryingAnotherPluginsKeyIsNotAdopted()
    {
        var ownership = new FakeCollectionOwnership();
        var theirs = ownership.Put(CollectionName, ("SomeOtherPlugin", RuleId));

        var resolved = await new CollectionResolver(ownership).ResolveAsync(Rule(), CancellationToken.None);

        Assert.NotEqual(theirs, resolved);
        Assert.Single(ownership.Created);
        Assert.Equal(
            new KeyValuePair<string, string>("SomeOtherPlugin", RuleId),
            Assert.Single(ownership.ProviderIdsOf(theirs)));
    }

    /// <summary>
    /// This plugin's own mark, carrying another rule's identity. The key matches and the value does
    /// not, which is the case a lookup asking only whether the key is present would get wrong:
    /// every collection this plugin ever made would answer it.
    /// </summary>
    [Fact]
    public async Task ACollectionThisPluginMarkedForAnotherRuleIsNotAdopted()
    {
        var ownership = new FakeCollectionOwnership();
        var other = ownership.Put("Eighties Thrillers", (CollectionStamp.PluginKey, "eighties-thrillers"));

        var resolved = await new CollectionResolver(ownership).ResolveAsync(Rule(), CancellationToken.None);

        Assert.NotEqual(other, resolved);
        Assert.Single(ownership.Created);
    }

    /// <summary>
    /// Two collections carrying one mark. This plugin does not produce that state and a restored
    /// backup can, and what a resolve may not do is answer differently depending on the order the
    /// server listed them in.
    /// </summary>
    /// <remarks>
    /// The identifiers are written here rather than generated, and the answer is named rather than
    /// left as whichever of the two both runs agreed on. A test asserting only that the two runs
    /// agree passes just as well against a resolve that takes the largest, and a resolve that
    /// takes the largest is not the one the tree documents: the two are told apart by the answer
    /// being the smaller, which is what this asserts.
    /// </remarks>
    [Fact]
    public async Task TwoCollectionsCarryingOneMarkResolveToTheSmallerWhateverOrderTheServerAnswersIn()
    {
        var smaller = new Guid("11111111-1111-1111-1111-111111111111");
        var larger = new Guid("22222222-2222-2222-2222-222222222222");

        var forwards = new FakeCollectionOwnership();
        forwards.Put(CollectionName, smaller, (CollectionStamp.PluginKey, RuleId));
        forwards.Put(CollectionName, larger, (CollectionStamp.PluginKey, RuleId));

        var backwards = new FakeCollectionOwnership { AnswersInReverse = true };
        backwards.Put(CollectionName, smaller, (CollectionStamp.PluginKey, RuleId));
        backwards.Put(CollectionName, larger, (CollectionStamp.PluginKey, RuleId));

        var one = await new CollectionResolver(forwards).ResolveAsync(Rule(), CancellationToken.None);
        var other = await new CollectionResolver(backwards).ResolveAsync(Rule(), CancellationToken.None);

        Assert.Equal(smaller, one);
        Assert.Equal(smaller, other);
        Assert.Empty(forwards.Created);
        Assert.Empty(backwards.Created);
    }

    /// <summary>
    /// The member the lookup is written against exists on both supported server lines. Its plural
    /// neighbour is one letter away, does the same job for several values at once, and is on 12.0
    /// only, so a package reaching for it would build here and throw a missing member on a 10.11
    /// server.
    /// </summary>
    /// <remarks>
    /// On the .NET 9 leg the plural does not exist to be set, so the assertion that it is unset is
    /// vacuous there and the leg that can go wrong is the .NET 10 one. The first assertion is the
    /// control: the singular is still the member it is named after, so a rename on either server
    /// line does not leave this test passing over nothing.
    /// </remarks>
    [Fact]
    public void TheLookupUsesTheProviderMemberBothServerLinesCarry()
    {
        var query = CollectionStamp.LookupQuery(RuleId);

        Assert.NotNull(typeof(InternalItemsQuery).GetProperty("HasAnyProviderId"));
        Assert.Equal(
            new KeyValuePair<string, string>(CollectionStamp.PluginKey, RuleId),
            Assert.Single(query.HasAnyProviderId!));

        var plural = typeof(InternalItemsQuery).GetProperty("HasAnyProviderIds");

        if (plural is not null)
        {
            Assert.Null(plural.GetValue(query));
        }
    }

    /// <summary>
    /// The lookup asks about collections. A query naming no kind would ask the whole library about
    /// a provider entry only a collection carries, on every resolve of every rule.
    /// </summary>
    [Fact]
    public void TheLookupAsksTheServerForCollections()
    {
        Assert.Equal(new[] { BaseItemKind.BoxSet }, CollectionStamp.LookupQuery(RuleId).IncludeItemTypes);
    }

    /// <summary>
    /// The query the resolve issues is the one the stamp declares, rather than one composed at the
    /// call site that could drift from what the create writes.
    /// </summary>
    [Fact]
    public async Task TheResolveLooksUpWithTheQueryTheStampDeclares()
    {
        var ownership = new FakeCollectionOwnership();

        await new CollectionResolver(ownership).ResolveAsync(Rule(), CancellationToken.None);

        var lookup = Assert.Single(ownership.Lookups);
        Assert.Equal(CollectionStamp.LookupQuery(RuleId).HasAnyProviderId, lookup.HasAnyProviderId);
        Assert.Equal(CollectionStamp.LookupQuery(RuleId).IncludeItemTypes, lookup.IncludeItemTypes);
    }

    /// <summary>
    /// A run cancelled before the create does not leave a collection behind. The server's create
    /// takes no cancellation token, so the port carries one and the check happens in front of it.
    /// </summary>
    [Fact]
    public async Task ACancelledResolveDoesNotCreateACollection()
    {
        var ownership = new FakeCollectionOwnership();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new CollectionResolver(ownership).ResolveAsync(Rule(), cancelled.Token));

        Assert.Empty(ownership.Created);
    }

    /// <summary>
    /// Neither the port nor the rule may be absent, and neither may the id the mark is made of.
    /// </summary>
    [Fact]
    public async Task TheResolveRefusesArgumentsThatAreNotThere()
    {
        Assert.Throws<ArgumentNullException>(() => new CollectionResolver(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new CollectionResolver(new FakeCollectionOwnership()).ResolveAsync(null!, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => CollectionStamp.For(null!));
        Assert.Throws<ArgumentNullException>(() => CollectionStamp.LookupQuery(null!));
    }

    private static RuleDocument Rule() => new(1, RuleId, CollectionName, "{}");
}
