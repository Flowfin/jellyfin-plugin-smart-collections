using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Membership;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The server as far as a resolve reaches it: collections carrying a name and a provider
/// dictionary, a lookup matching on a key and its value together the way the server's own query
/// does, and a create that writes what it was handed.
/// </summary>
internal sealed class FakeCollectionOwnership : ICollectionOwnership
{
    private readonly List<FakeCollection> _collections = new();

    /// <summary>
    /// Gets a value indicating whether the lookup answers in the reverse of the order the
    /// collections were put here in. A query returns rows in whatever order the store produced
    /// them, and nothing in this plugin may depend on which.
    /// </summary>
    public bool AnswersInReverse { get; init; }

    public IReadOnlyList<FakeCollection> Collections => _collections;

    public List<InternalItemsQuery> Lookups { get; } = new();

    public List<FakeCollection> Created { get; } = new();

    public Guid Put(string name, params (string Key, string Value)[] providerIds)
        => Put(name, Guid.NewGuid(), providerIds);

    public Guid Put(string name, Guid id, params (string Key, string Value)[] providerIds)
    {
        var collection = new FakeCollection(
            id,
            name,
            providerIds.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

        _collections.Add(collection);

        return collection.Id;
    }

    public string NameOf(Guid id) => Held(id).Name;

    public IReadOnlyDictionary<string, string> ProviderIdsOf(Guid id) => Held(id).ProviderIds;

    public IReadOnlyList<Guid> FindCollections(InternalItemsQuery lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        Lookups.Add(lookup);

        var wanted = lookup.HasAnyProviderId;

        if (wanted is null || wanted.Count == 0)
        {
            throw new InvalidOperationException(
                "A lookup naming no provider entry would match every collection on the server.");
        }

        // Both arms the server's own translation of this member has. A pair carrying a value
        // matches the key and the value together; a pair carrying an empty one matches every
        // item that has the provider at all. The second arm is what makes a lookup written
        // with the key alone find every collection this plugin ever created, and a fake
        // holding only the first arm would report that mistake as no match rather than as the
        // wrong match it is.
        var found = _collections
            .Where(collection => wanted.Any(pair
                => collection.ProviderIds.TryGetValue(pair.Key, out var held)
                    && (string.IsNullOrEmpty(pair.Value)
                        || string.Equals(held, pair.Value, StringComparison.Ordinal))))
            .Select(collection => collection.Id)
            .ToList();

        if (AnswersInReverse)
        {
            found.Reverse();
        }

        return found;
    }

    public Task<Guid> CreateCollectionAsync(
        string name,
        IReadOnlyDictionary<string, string> providerIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(providerIds);
        cancellationToken.ThrowIfCancellationRequested();

        var collection = new FakeCollection(
            Guid.NewGuid(),
            name,
            new Dictionary<string, string>(providerIds, StringComparer.Ordinal));

        _collections.Add(collection);
        Created.Add(collection);

        return Task.FromResult(collection.Id);
    }

    public Task DeleteCollectionAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        _collections.RemoveAll(collection => collection.Id == collectionId);
        return Task.CompletedTask;
    }

    private FakeCollection Held(Guid id) => _collections.Single(collection => collection.Id == id);
}

/// <summary>
/// One collection as this fake holds it.
/// </summary>
/// <param name="Id">What the server would call it.</param>
/// <param name="Name">What a library shows.</param>
/// <param name="ProviderIds">The marks on it, this plugin's among them or not.</param>
internal sealed record FakeCollection(Guid Id, string Name, IReadOnlyDictionary<string, string> ProviderIds);
