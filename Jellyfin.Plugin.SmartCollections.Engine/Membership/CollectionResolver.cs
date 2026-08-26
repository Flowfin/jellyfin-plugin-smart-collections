using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Rules;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// Turns a loaded rule into the collection that rule owns, creating it the first time.
/// </summary>
/// <remarks>
/// Every path that changes a collection takes the collection as an argument:
/// <see cref="CollectionRefresh"/> is an identifier and a diff, and
/// <see cref="ICollectionMembershipWriter"/> writes to an identifier it is handed. This is where
/// that identifier comes from, and it is meant to be the only place, because the two ways of
/// getting one wrong are both silent.
///
/// The first is adopting by name. A collection called what the rule calls its collection is not
/// necessarily this plugin's: an operator may have built one by hand, with items they chose, and a
/// refresh that adopted it would empty it of everything the rule does not match. So the lookup is
/// by mark and never by name, and an unmarked collection sharing the name is left alone and a new
/// one is created beside it. That leaves two collections with one name in the library, which is
/// visible and recoverable; writing into somebody's hand-made collection is neither.
///
/// The second is creating a second collection every run. That is what happens when the mark is not
/// written by the create, or when the lookup asks for something the create did not write, and its
/// symptom is a library filling up with copies. The mark written and the mark looked for are one
/// declaration in <see cref="CollectionStamp"/> for that reason.
///
/// Nothing here writes membership, and nothing here deletes. A rule whose collection an operator
/// deleted gets a new one on the next resolve, because the rule is the declaration and the
/// collection is what it produces.
/// </remarks>
public sealed class CollectionResolver
{
    private readonly ICollectionOwnership _ownership;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionResolver"/> class.
    /// </summary>
    /// <param name="ownership">The port the lookup and the create go through.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ownership"/> is <see langword="null"/>.</exception>
    public CollectionResolver(ICollectionOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        _ownership = ownership;
    }

    /// <summary>
    /// Resolves the collection a rule owns.
    /// </summary>
    /// <param name="rule">The rule, as it was loaded and validated.</param>
    /// <param name="cancellationToken">Cancels the create, where one is needed.</param>
    /// <returns>The identifier of the collection this rule owns.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <see langword="null"/>.</exception>
    public async Task<Guid> ResolveAsync(RuleDocument rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var marked = _ownership.FindCollections(CollectionStamp.LookupQuery(rule.Id));

        if (marked.Count > 0)
        {
            return Adopted(marked);
        }

        return await _ownership.CreateCollectionAsync(
            rule.Name,
            CollectionStamp.For(rule.Id),
            cancellationToken).ConfigureAwait(false);
    }

    // Which collection a rule owns when several carry its mark. Two of them is a state this
    // plugin does not create, because the mark is written once and by the create, and it is
    // reachable anyway: a restored backup, or a collection somebody copied with its provider
    // entries. Answering with whatever the server listed first would make a rule own one
    // collection today and another tomorrow off nothing but the order a query came back in, which
    // is the failure the determinism milestone is about, arriving through the one call that looks
    // too small to carry it. The smallest identifier is arbitrary and total, and total is the
    // property that matters: the same set answers the same way on every run, on either server
    // line, whatever order it arrived in.
    private static Guid Adopted(IReadOnlyList<Guid> marked)
    {
        var owner = marked[0];

        for (var index = 1; index < marked.Count; index++)
        {
            if (marked[index].CompareTo(owner) < 0)
            {
                owner = marked[index];
            }
        }

        return owner;
    }
}
