using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// Every property of a server item query, rendered so that two queries can be compared without
/// asking each property type for an equality it may not have.
/// </summary>
/// <remarks>
/// The set of properties is taken by reflecting over the type the build resolved rather than from a
/// list of names. The two supported server lines carry different numbers of properties, so a
/// checked-in list would red one leg of the suite for a reason that has nothing to do with this
/// plugin.
///
/// WHAT THIS CANNOT SEE is two things, and both are written down rather than left for a later
/// reader to discover. A property whose value is an object with no value equality renders as its
/// type name, so a change made inside such an object is invisible here. And a property with no
/// getter cannot be read at all: both lines carry exactly one, <c>Parent</c>.
/// </remarks>
public static class QuerySnapshot
{
    /// <summary>
    /// Renders every readable property of a query.
    /// </summary>
    /// <param name="query">The query to read.</param>
    /// <returns>One entry per readable property, by name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    public static IReadOnlyDictionary<string, string> Of(InternalItemsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return typeof(InternalItemsQuery)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead)
            .ToDictionary(
                property => property.Name,
                property => Render(property.GetValue(query)),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// The properties a query carries that a freshly constructed one does not, by name and in
    /// order.
    /// </summary>
    /// <param name="query">The query to read.</param>
    /// <returns>The names, sorted.</returns>
    public static string[] Moved(InternalItemsQuery query)
    {
        var baseline = Of(new InternalItemsQuery());

        return Of(query)
            .Where(entry => !string.Equals(baseline[entry.Key], entry.Value, StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Render(object? value) => value switch
    {
        null => "<null>",
        string text => text,
        IEnumerable items => "["
            + string.Join(", ", items.Cast<object?>().Select(item => item?.ToString() ?? "<null>"))
            + "]",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.GetType().Name
    };
}
