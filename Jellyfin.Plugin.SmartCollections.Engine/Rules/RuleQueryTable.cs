using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// The pairs of a field and an operator the server's own item query answers, and the property on
/// <see cref="InternalItemsQuery"/> each one writes.
/// </summary>
/// <remarks>
/// This is the table the compiler is made of. It is declared rather than derived, for the reason
/// the field table and the operator table are: what the server's query means by a property is a
/// fact about the server, and a table that inferred it from a name would be a guess that compiles.
///
/// Every row here was read off the server's own translation of the query rather than off the
/// property's name. Taken at <c>v10.11.11</c>, which is the older of the two lines this plugin
/// ships for and therefore the one that bounds what may be written:
///
/// <code>
/// gh api "repos/jellyfin/jellyfin/contents/Jellyfin.Server.Implementations/Item/BaseItemRepository.cs?ref=v10.11.11" \
///   --jq .content | base64 -d | grep -nE 'MinCommunityRating|MinDateCreated|MinPremiereDate|MaxPremiereDate|filter.Genres|filter.OfficialRatings|filter.Years'
/// </code>
///
/// WHAT THE TABLE DOES NOT CLAIM is that the comparison behind a row is an ordinal one. The
/// server compares its own cleaned form of a name, a genre and a tag, so <c>name equals</c> is the
/// server's equality over titles and not a byte comparison. Which comparison this plugin names
/// everywhere is a question of its own and is not answered by a row being here.
/// </remarks>
public static class RuleQueryTable
{
    private static readonly RuleQueryRow[] Table =
    [
        new(
            RuleField.CommunityRating,
            RuleOperator.GreaterThanOrEqual,
            "MinCommunityRating",
            "The community rating is the value or above it.",
            (query, values) =>
            {
                query.MinCommunityRating = (double)Decimal(values[0]);
                return true;
            }),
        new(
            RuleField.DateAdded,
            RuleOperator.After,
            "MinDateCreated",
            "The server first saw the item after the value.",
            (query, values) => After(Instant(values[0]), instant => query.MinDateCreated = instant)),
        new(
            RuleField.Genres,
            RuleOperator.Contains,
            "Genres",
            "The item carries the genre.",
            (query, values) =>
            {
                query.Genres = Text(values);
                return true;
            }),
        new(
            RuleField.Name,
            RuleOperator.Equals,
            "Name",
            "The title is the value.",
            (query, values) =>
            {
                query.Name = Text(values)[0];
                return true;
            }),
        new(
            RuleField.OfficialRating,
            RuleOperator.Equals,
            "OfficialRatings",
            "The age classification is the value.",
            (query, values) =>
            {
                query.OfficialRatings = Text(values);
                return true;
            }),
        new(
            RuleField.OfficialRating,
            RuleOperator.In,
            "OfficialRatings",
            "The age classification is one of the values.",
            (query, values) =>
            {
                query.OfficialRatings = Text(values);
                return true;
            }),
        new(
            RuleField.PremiereDate,
            RuleOperator.After,
            "MinPremiereDate",
            "The item was first released after the value.",
            (query, values) => After(Instant(values[0]), instant => query.MinPremiereDate = instant)),
        new(
            RuleField.PremiereDate,
            RuleOperator.Before,
            "MaxPremiereDate",
            "The item was first released before the value.",
            (query, values) => Before(Instant(values[0]), instant => query.MaxPremiereDate = instant)),
        new(
            RuleField.ProductionYear,
            RuleOperator.Equals,
            "Years",
            "The production year is the value.",
            (query, values) => Years(values, years => query.Years = years)),
        new(
            RuleField.ProductionYear,
            RuleOperator.In,
            "Years",
            "The production year is one of the values.",
            (query, values) => Years(values, years => query.Years = years)),
        new(
            RuleField.Tags,
            RuleOperator.Contains,
            "Tags",
            "The item carries the tag.",
            (query, values) =>
            {
                query.Tags = Text(values);
                return true;
            }),
        new(
            RuleField.Tags,
            RuleOperator.NotContains,
            "ExcludeTags",
            "The item carries the tag nowhere.",
            (query, values) =>
            {
                query.ExcludeTags = Text(values);
                return true;
            })
    ];

    /// <summary>
    /// Gets every row, in the order the table declares them.
    /// </summary>
    public static IReadOnlyList<RuleQueryRow> Rows => Table;

    /// <summary>
    /// Finds the row for a pair, where the query answers it.
    /// </summary>
    /// <param name="field">The field a condition names.</param>
    /// <param name="operator">The operator a condition applies.</param>
    /// <returns>The row, or <see langword="null"/> where the query does not answer this pair.</returns>
    /// <remarks>
    /// A null answer is an ordinary one and never a fault. It says the comparison is one this
    /// plugin accepts and the server's query cannot express, which is every pair the field table
    /// allows and this table has no row for.
    /// </remarks>
    public static RuleQueryRow? Find(RuleField field, RuleOperator @operator)
    {
        foreach (var row in Table)
        {
            if (row.Field == field && row.Operator == @operator)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// Answers whether the query narrows on a field at all.
    /// </summary>
    /// <param name="field">The field to ask about.</param>
    /// <returns><see langword="true"/> where at least one pair over this field is compiled.</returns>
    public static bool Narrows(RuleField field) => Table.Any(row => row.Field == field);

    private static decimal Decimal(RuleValue value) => (decimal)value.Value;

    private static DateTimeOffset Instant(RuleValue value) => (DateTimeOffset)value.Value;

    private static string[] Text(IReadOnlyList<RuleValue> values)
    {
        var text = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            text[index] = (string)values[index].Value;
        }

        return text;
    }

    /// <summary>
    /// Writes an at-or-after comparison that means an after one.
    /// </summary>
    /// <remarks>
    /// The server compares the stored instant against the value with <c>&gt;=</c>, and the
    /// operator's own sentence is that the field is LATER than the value. A .NET instant is a whole
    /// number of ticks, so <c>t &gt; x</c> and <c>t &gt;= x + 1 tick</c> select the same instants
    /// and the offset is the exact translation rather than an approximation of one. The last tick
    /// a date can name has no room for it, which is the one document this cannot carry.
    /// </remarks>
    private static bool After(DateTimeOffset value, Action<DateTime> write)
    {
        var instant = value.UtcDateTime;
        if (instant == DateTime.MaxValue)
        {
            return false;
        }

        write(instant.AddTicks(1));
        return true;
    }

    /// <summary>
    /// Writes an at-or-before comparison that means a before one, by the reasoning
    /// <see cref="After"/> gives, in the other direction.
    /// </summary>
    private static bool Before(DateTimeOffset value, Action<DateTime> write)
    {
        var instant = value.UtcDateTime;
        if (instant == DateTime.MinValue)
        {
            return false;
        }

        write(instant.AddTicks(-1));
        return true;
    }

    /// <summary>
    /// Writes a year list, where every year the document wrote fits the query's own year type.
    /// </summary>
    /// <remarks>
    /// A production year is declared as a whole number, which this plugin reads to the full range
    /// of a 64-bit integer, and the query holds years as 32-bit integers. A document naming a year
    /// outside that range is a document this plugin accepts and this property cannot carry, so it
    /// is handed on rather than narrowed here.
    /// </remarks>
    private static bool Years(IReadOnlyList<RuleValue> values, Action<int[]> write)
    {
        var years = new int[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var year = (long)values[index].Value;
            if (year < int.MinValue || year > int.MaxValue)
            {
                return false;
            }

            years[index] = (int)year;
        }

        write(years);
        return true;
    }
}
