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
    private static DateTimeOffset Instant(DateTime value) => Instant(value, TimeZoneInfo.Local);

    /// <summary>
    /// The same reading, out of a named zone rather than out of the one the machine happens to sit in.
    /// </summary>
    /// <param name="value">The value the library holds.</param>
    /// <param name="server">The zone a value that carries a kind is converted out of.</param>
    /// <returns>The instant.</returns>
    /// <remarks>
    /// THE ZONE IS A PARAMETER SO THE TWO ARMS BELOW CAN BE TOLD APART WHEREVER THE SUITE RUNS.
    /// Where the zone is UTC the conversion and the relabelling compute the same instant for every
    /// input, so a suite running there separates the arms by nothing: three seeded faults at that
    /// expression are killed in a clone whose zone is not UTC and survived on the runner, which is
    /// UTC, and #267 carries both readings. The reading above passes the machine's zone and is the
    /// one an evaluation takes; a test passes a zone with an offset of its own, and the two arms
    /// then answer differently on any machine.
    /// </remarks>
    internal static DateTimeOffset Instant(DateTime value, TimeZoneInfo server)
        => value.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
            : new DateTimeOffset(Universal(value, server), TimeSpan.Zero);

    /// <summary>
    /// A value that carries a kind, in UTC.
    /// </summary>
    /// <param name="value">The value the library holds, carrying a kind.</param>
    /// <param name="server">The zone the value is converted out of where it is not already UTC.</param>
    /// <returns>The same instant, labelled UTC.</returns>
    /// <remarks>
    /// <see cref="DateTime.ToUniversalTime"/> reads <see cref="TimeZoneInfo.Local"/> and cannot be
    /// pointed at another zone, so the offset is taken from the zone above instead. A value already
    /// in UTC is returned rather than shifted, which is what that method does with one, and the
    /// kind is dropped before the offset is asked for so the zone above is the one that answers
    /// rather than the machine's.
    /// </remarks>
    private static DateTime Universal(DateTime value, TimeZoneInfo server)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        var wall = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return DateTime.SpecifyKind(wall - server.GetUtcOffset(wall), DateTimeKind.Utc);
    }

    /// <summary>
    /// A list of strings the library holds, with a null read as none.
    /// </summary>
    /// <param name="values">The array the library holds.</param>
    /// <returns>The strings.</returns>
    private static string[] Strings(string[]? values) => values ?? [];
}
