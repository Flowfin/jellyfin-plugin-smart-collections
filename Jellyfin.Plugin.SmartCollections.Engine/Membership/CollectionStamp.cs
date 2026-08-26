using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// The mark this plugin writes onto a collection it created, and the query that finds it again.
/// </summary>
/// <remarks>
/// A rule and the collection it owns have to find each other across a restart, an upgrade and a
/// rename on either side. Three things could carry that pairing and two of them are wrong. The
/// collection's name changes when an operator renames the collection, which is a thing they are
/// entitled to do. A file name changes when they tidy the rules directory. What is left is a mark
/// on the collection itself, written when it is created and read back afterwards, and the server
/// already holds one per item for exactly this purpose: the provider dictionary, which is a set of
/// key and value pairs an item carries and a query can filter on.
///
/// So the pairing is a provider entry whose key is this plugin and whose value is the rule's id.
/// The id is the member that never changes when a name or a file does, which is what
/// <see cref="Rules.RuleDocument.Id"/> exists for, and the mark inherits that.
///
/// <para>
/// The key is <c>SmartCollections</c>, the same string the plugin's own directory under the
/// server's configuration path is named with. It is a name rather than the plugin's identifier
/// because it is stored per item and read by anyone looking at an item's provider list through the
/// server's API, where a bare identifier says nothing about what put it there. The residual is
/// that nothing reserves the string: a second plugin choosing the same key and the same value
/// would produce a collection this plugin would adopt. That is not a case this tree can refuse,
/// and it is stated here rather than left to be discovered.
/// </para>
/// </remarks>
public static class CollectionStamp
{
    /// <summary>
    /// The provider key every collection this plugin created carries.
    /// </summary>
    public const string PluginKey = "SmartCollections";

    /// <summary>
    /// The mark a collection owned by this rule carries.
    /// </summary>
    /// <param name="ruleId">The rule's identity, as the document declares it.</param>
    /// <returns>The provider entries to write onto the collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ruleId"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Ordinal, because the validator has already refused every id this dictionary could be asked
    /// to fold: an id is lowercase ASCII and a hyphen, so a comparer that folded would agree with
    /// this one today and stop agreeing the day that set widens.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> For(string ruleId)
    {
        ArgumentNullException.ThrowIfNull(ruleId);

        return new Dictionary<string, string>(StringComparer.Ordinal) { [PluginKey] = ruleId };
    }

    /// <summary>
    /// The query that finds the collection this rule owns.
    /// </summary>
    /// <param name="ruleId">The rule's identity, as the document declares it.</param>
    /// <returns>A query for the collections carrying this rule's mark.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ruleId"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <see cref="InternalItemsQuery.HasAnyProviderId"/> and never
    /// <c>HasAnyProviderIds</c>. The two differ by one letter and by which server lines carry
    /// them: the singular is on 10.11 and on 12.0, the plural is on 12.0 only. The plural is what
    /// a lookup of several rules at once reaches for, since it takes a list of values per key, and
    /// reaching for it here would produce a package that compiles against the newer server and
    /// throws a missing member on the older one - at run time, on an operator's server, rather
    /// than in a build. <c>TheLookupUsesTheMemberBothServerLinesCarry</c> is what goes red if it
    /// arrives by a later edit.
    ///
    /// A value that is not empty makes the server match the key and the value together, so a
    /// collection carrying this plugin's key with another rule's id is not returned.
    ///
    /// The kind is named because the mark is written on a collection and a query that named no
    /// kind would ask the whole library about a provider entry only a collection carries. No
    /// parent is named: a collection this plugin created lives wherever the server puts one, and a
    /// lookup bounded to a folder would stop finding a collection an operator moved.
    /// </remarks>
    public static InternalItemsQuery LookupQuery(string ruleId)
    {
        ArgumentNullException.ThrowIfNull(ruleId);

        return new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            HasAnyProviderId = new Dictionary<string, string>(StringComparer.Ordinal) { [PluginKey] = ruleId }
        };
    }
}
