using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Configuration;
using Jellyfin.Plugin.SmartCollections.Library;
using Jellyfin.Plugin.SmartCollections.Membership;
using Jellyfin.Plugin.SmartCollections.Rules;
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
    /// invariant form the value round-trips in rather than in words.
    /// </summary>
    private static readonly Regex Row = new(
        @"^\|\s*`(?<name>[A-Za-z][A-Za-z0-9]*)`\s*\|\s*`(?<value>[0-9][0-9:.]*)`\s*\|",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// The types that declare a value this page is answerable for. This is the one list the test
    /// carries, and it is a list of TYPES rather than of values: a field added to either of them
    /// is covered on the day it appears. A third type declaring one has to be added here, and
    /// <see cref="EveryDefaultTheShippedAssembliesDeclareIsOnATypeThisTestReads"/> is what says so
    /// rather than leaving the value outside the page in silence.
    /// </summary>
    private static readonly Type[] Declaring =
        [typeof(LibraryChangeCoalescer), typeof(CollectionRefreshHistory)];

    /// <summary>
    /// The assemblies a value this page is answerable for can be declared in. Each is named by a
    /// type that declares no default of its own, so the search below is not anchored on the same
    /// two types the list above holds.
    /// </summary>
    private static readonly Assembly[] Shipped =
        [typeof(PluginConfiguration).Assembly, typeof(RuleDocument).Assembly];

    /// <summary>
    /// Reads the values the page is answerable for out of the types that declare them, rather
    /// than from a list this test would carry. A fourth value declared tomorrow is covered on the
    /// day the field appears and needs no edit here.
    /// </summary>
    /// <returns>Each public static field, by name, written as the page has to write it.</returns>
    private static IReadOnlyDictionary<string, string> DeclaredDefaults()
        => Declaring
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .ToDictionary(
                field => field.Name,
                Written,
                StringComparer.Ordinal);

    /// <summary>
    /// How the page has to write one value. A kind this does not know throws rather than being
    /// skipped, because skipping it would take the field out of the page's obligation silently,
    /// which is the failure the whole comparison exists against.
    /// </summary>
    /// <param name="field">The field the page is answerable for.</param>
    /// <returns>The value in the invariant form a row carries.</returns>
    private static string Written(FieldInfo field)
        => field.GetValue(null) switch
        {
            TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            var other => throw new InvalidOperationException(
                field.DeclaringType!.Name + "." + field.Name + " is a "
                + (other?.GetType().Name ?? "null")
                + ", and this test writes a duration and a whole number. Teach it that kind here,"
                + " or the value reaches no row on " + Page + " and nothing says so."),
        };

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

            Assert.Equal(value, written);
        }
    }

    /// <summary>
    /// The pair above compares the page against the types <see cref="Declaring"/> names, so a
    /// default declared anywhere else is outside the comparison and outside the page at once, and
    /// both runs stay green while a value nobody wrote down ships. This walks the shipped
    /// assemblies instead of the list, and refuses a default the list does not reach.
    /// </summary>
    /// <remarks>
    /// What it reads is a NAME. That is a bound of its own rather than the absence of one: a
    /// default called something else is seen by neither this test nor the pair above. The trade is
    /// deliberate. A type is invisible in the diff that adds a field to it, and a name is in that
    /// diff, so a reviewer meeting a value called <c>MaximumWait</c> is being asked about
    /// something in front of them, where a reviewer meeting a value on a new type was being asked
    /// about nothing.
    ///
    /// It refuses rather than widening the comparison to whatever it finds, because whether a
    /// value is one this page is answerable for is a judgement. The same assemblies declare the
    /// nesting limit, the identifier length and the schema versions, and those are rule-language
    /// limits with pages of their own. So the failure hands that judgement to whoever added the
    /// field instead of taking it here.
    /// </remarks>
    [Fact]
    public void EveryDefaultTheShippedAssembliesDeclareIsOnATypeThisTestReads()
    {
        var read = Declaring.ToHashSet();

        foreach (var field in DeclaredDefaultFields())
        {
            Assert.True(
                read.Contains(field.DeclaringType!),
                field.DeclaringType!.FullName + "." + field.Name + " is a default the shipped"
                + " assemblies declare, and no type this test reads declares it, so it reaches no"
                + " row on " + Page + " and nothing else would say so. Add its type to the list in"
                + " this file together with its row, or write on the page why the value is not one"
                + " this page is answerable for.");
        }
    }

    /// <summary>
    /// Without this the test above passes on a search that has stopped finding anything, which is
    /// what an assembly this list no longer names, or a prefix nobody writes any more, would leave
    /// behind.
    /// </summary>
    [Fact]
    public void TheSearchOverTheShippedAssembliesFindsTheDefaultsThatAreOnThePage()
    {
        Assert.Equal(
            DeclaredDefaults().Keys.OrderBy(name => name, StringComparer.Ordinal),
            DeclaredDefaultFields().Select(field => field.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every default the shipped assemblies declare, wherever it is declared. Internal types are
    /// walked too: a value is no less real for sitting on a type nothing outside its assembly can
    /// name, and what the page owes is about the value rather than about who can reach it.
    /// </summary>
    /// <returns>Each public static field whose name begins with <c>Default</c>.</returns>
    private static IReadOnlyList<FieldInfo> DeclaredDefaultFields()
        => Shipped
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetFields(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(field => field.Name.StartsWith("Default", StringComparison.Ordinal))
            .OrderBy(field => field.DeclaringType!.FullName, StringComparer.Ordinal)
            .ThenBy(field => field.Name, StringComparer.Ordinal)
            .ToList();
}
