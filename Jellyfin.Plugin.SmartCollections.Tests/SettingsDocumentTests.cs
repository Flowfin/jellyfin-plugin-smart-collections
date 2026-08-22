using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Library;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The reason a default is what it is lives in <c>docs/settings.md</c>, and a reason that has
/// drifted from the value it explains is worse than no page: somebody reads why thirty seconds
/// was chosen, changes the number to five minutes for an unrelated reason, and leaves the page
/// arguing for a value the plugin no longer uses. These tests compare the page against the
/// fields the plugin declares, in both directions.
/// </summary>
public class SettingsDocumentTests
{
    private const string Page = "docs/settings.md";

    /// <summary>
    /// A row of the table on that page. The name and the default are both in backticks so a row
    /// is told from prose that happens to mention a field, and the default is written in the
    /// invariant form <see cref="TimeSpan"/> round-trips rather than in words.
    /// </summary>
    private static readonly Regex Row = new(
        @"^\|\s*`(?<name>[A-Za-z][A-Za-z0-9]*)`\s*\|\s*`(?<value>[0-9][0-9:.]*)`\s*\|",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the values the page is answerable for out of the type that declares them, rather
    /// than from a list this test would carry. A fourth interval declared tomorrow is covered
    /// on the day the field appears and needs no edit here.
    /// </summary>
    /// <returns>Each public static <see cref="TimeSpan"/> field, by name.</returns>
    private static IReadOnlyDictionary<string, TimeSpan> DeclaredDefaults()
        => typeof(LibraryChangeCoalescer)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(TimeSpan))
            .ToDictionary(
                field => field.Name,
                field => (TimeSpan)field.GetValue(null)!,
                StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> DocumentedDefaults()
        => Row.Matches(RepositoryFiles.ReadFromRoot(Page))
            .ToDictionary(
                row => row.Groups["name"].Value,
                row => row.Groups["value"].Value,
                StringComparer.Ordinal);

    /// <summary>
    /// Without this the pair below passes on a page whose table somebody deleted, because two
    /// empty sets agree and every documented value is then trivially correct.
    /// </summary>
    [Fact]
    public void ThePageCarriesATable()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryFiles.Root(), Page)), Page + " is not in the tree.");
        Assert.NotEmpty(DocumentedDefaults());
    }

    [Fact]
    public void EveryDeclaredDefaultHasARowOnThePage()
    {
        Assert.Equal(
            DeclaredDefaults().Keys.OrderBy(name => name, StringComparer.Ordinal),
            DocumentedDefaults().Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryRowOnThePageHoldsTheValueTheFieldDeclares()
    {
        var declared = DeclaredDefaults();

        foreach (var (name, written) in DocumentedDefaults())
        {
            Assert.True(declared.TryGetValue(name, out var value), "No field is named " + name + ".");

            Assert.True(
                TimeSpan.TryParse(written, CultureInfo.InvariantCulture, out var documented),
                Page + " writes the default for " + name + " as " + written + ", which is not a duration.");

            Assert.Equal(value, documented);
        }
    }
}
