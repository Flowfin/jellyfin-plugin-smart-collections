using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SmartCollections.Evaluation;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The server as far as an evaluation reaches it: a list of items, a record of every query it was
/// asked, and an order it can be told to answer in.
/// </summary>
/// <remarks>
/// The queries are kept rather than the count, because a test that only counted could not tell a
/// step that asked the right query from one that asked an unnarrowed one, and an unnarrowed query
/// against a real server is the whole library.
///
/// <see cref="AnswersInReverse"/> exists for the reason the collection fake's own does: the server
/// answers in whatever order its store produced, and nothing this plugin builds may depend on
/// which. A test that never varies the order cannot see a step that passes the server's order
/// through.
/// </remarks>
internal sealed class FakeRuleItemSource : IRuleItemSource
{
    private readonly List<BaseItem> _items = new();

    private Random? _shuffle;

    /// <summary>
    /// Gets a value indicating whether this answers in the reverse of the order it was filled.
    /// </summary>
    public bool AnswersInReverse { get; init; }

    /// <summary>
    /// Gets the seed this shuffles its answer with, or <see langword="null"/> where it does not
    /// shuffle.
    /// </summary>
    /// <remarks>
    /// One generator for the life of the fake rather than one per call, so consecutive calls get
    /// DIFFERENT orders and the whole sequence is still decided by the seed. A generator built
    /// per call from one seed would answer in the same order every time, which is the arrangement
    /// this exists to rule out.
    ///
    /// Where a seed is set it decides the order and <see cref="AnswersInReverse"/> is not read.
    /// The two are separate arrangements: one is a fixed order that is not the fill order, the
    /// other is a different order on every call.
    /// </remarks>
    public int? Seed { get; init; }

    /// <summary>
    /// Gets every query this was asked, in the order it was asked.
    /// </summary>
    public List<InternalItemsQuery> Asked { get; } = new();

    /// <summary>
    /// Puts an item in the library this stands for.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The item, so a test can name its identifier in the same expression.</returns>
    public BaseItem Put(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _items.Add(item);

        return item;
    }

    /// <inheritdoc />
    public IReadOnlyList<BaseItem> Select(InternalItemsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        Asked.Add(query);

        var answer = new List<BaseItem>(_items);

        if (Seed.HasValue)
        {
            _shuffle ??= new Random(Seed.Value);

            for (var index = answer.Count - 1; index > 0; index--)
            {
                var swap = _shuffle.Next(index + 1);
                (answer[index], answer[swap]) = (answer[swap], answer[index]);
            }

            return answer;
        }

        if (AnswersInReverse)
        {
            answer.Reverse();
        }

        return answer;
    }
}
