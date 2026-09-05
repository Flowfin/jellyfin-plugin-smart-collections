using System;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Evaluation;

/// <summary>
/// Reads one field of this plugin's vocabulary off one item the server answered with.
/// </summary>
/// <remarks>
/// One switch over the declared vocabulary rather than a lookup by name. The prior art in this
/// space resolves a document's field string as a property with <c>Expression.PropertyOrField</c>,
/// so what a field means is whatever member happens to sit on the server's type that day, and a
/// member the server renames turns every rule using it into a runtime exception. Here a field is
/// an enumeration member with a row, and the arm below is where that member is tied to the item
/// member it is about - so the same rename is a compile error in this file instead.
///
/// NOTHING HERE ASKS THE LIBRARY FOR ANYTHING. Every arm reads a member of the item the query
/// already returned, which is the property #31 asks the post-query stage to hold: the stage runs
/// over the query result and never over the library, so an evaluation makes one call to the server
/// whatever a rule says.
///
/// THE TWO INSTANT FIELDS ARE READ AS UTC WHERE THE LIBRARY LEAVES THE KIND UNSPECIFIED. The
/// server stores both in UTC and a value with no kind is one that lost its label on the way out of
/// a database, so reading it as local time would make a rule saying "released before 2001" answer
/// differently on two servers in two zones, which is the property this plugin exists to hold. A
/// value that does carry a kind is converted rather than relabelled.
/// </remarks>
public static class ItemFieldReader
{
    /// <summary>
    /// Reads a field off an item.
    /// </summary>
    /// <param name="item">The item, as the server answered with it.</param>
    /// <param name="field">The field to read.</param>
    /// <returns>The value, in the shape the field takes on an item.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="field"/> has no arm here.</exception>
    public static ItemFieldReading Read(BaseItem item, RuleField field)
    {
        ArgumentNullException.ThrowIfNull(item);

        return field switch
        {
            RuleField.CommunityRating => ItemFieldReading.OfNumber(
                item.CommunityRating.HasValue ? (decimal)item.CommunityRating.Value : null),
            RuleField.DateAdded => ItemFieldReading.OfInstant(Instant(item.DateCreated)),
            RuleField.Genres => ItemFieldReading.OfTextList(Strings(item.Genres)),
            RuleField.Name => ItemFieldReading.OfText(item.Name),
            RuleField.OfficialRating => ItemFieldReading.OfText(item.OfficialRating),
            RuleField.Overview => ItemFieldReading.OfText(item.Overview),
            RuleField.PremiereDate => ItemFieldReading.OfInstant(
                item.PremiereDate.HasValue ? Instant(item.PremiereDate.Value) : null),
            RuleField.ProductionYear => ItemFieldReading.OfNumber(
                item.ProductionYear.HasValue ? item.ProductionYear.Value : null),
            RuleField.Runtime => ItemFieldReading.OfSpan(
                item.RunTimeTicks.HasValue ? TimeSpan.FromTicks(item.RunTimeTicks.Value) : null),
            RuleField.Tags => ItemFieldReading.OfTextList(Strings(item.Tags)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(field),
                field,
                "No arm reads this field off an item. A field added to the vocabulary owes one here.")
        };
    }

    /// <summary>
    /// An instant the library holds, read as UTC where it carries no kind.
    /// </summary>
    /// <param name="value">The value the library holds.</param>
    /// <returns>The instant.</returns>
    private static DateTimeOffset Instant(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
            : new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero);

    /// <summary>
    /// A list of strings the library holds, with a null read as none.
    /// </summary>
    /// <param name="values">The array the library holds.</param>
    /// <returns>The strings.</returns>
    private static string[] Strings(string[]? values) => values ?? [];
}
