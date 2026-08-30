using System;
using System.Reflection;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A field row that names a query property is a promise that the property is there on every
/// server this plugin ships for. These tests read that off the <c>InternalItemsQuery</c> the suite
/// is compiled against rather than off a list somebody typed, so the promise is checked against
/// the surface the package will actually meet.
/// </summary>
/// <remarks>
/// The suite builds for both target frameworks, and each one resolves the SDK for one supported
/// server line. So the net9.0 leg is the 10.11 reading and the net10.0 leg is the 12.0 one, and a
/// property added to the newer line and named in a row reds the older leg rather than reaching a
/// user as a missing member. The first test below asserts which line each leg is, so that claim
/// rests on a reading rather than on the project file staying as it is.
/// </remarks>
public class RuleFieldQuerySurfaceTests
{
    private static Version ServerLine()
        => typeof(InternalItemsQuery).Assembly.GetName().Version
           ?? throw new InvalidOperationException("The server assembly carries no version.");

    [Fact]
    public void EachLegOfTheSuiteIsCompiledAgainstTheServerLineItIsFor()
    {
        var line = ServerLine();

#if NET9_0
        Assert.Equal(10, line.Major);
        Assert.Equal(11, line.Minor);
#else
        Assert.Equal(12, line.Major);
#endif
    }

    /// <summary>
    /// The done condition this test carries: every row naming a query property names one that is
    /// present.
    /// </summary>
    [Fact]
    public void EveryRowThatNamesAQueryPropertyNamesOneTheServerQueryCarries()
    {
        foreach (var row in RuleFieldTable.Rows)
        {
            if (row.QueryProperty is null)
            {
                continue;
            }

            Assert.True(
                typeof(InternalItemsQuery).GetProperty(row.QueryProperty, BindingFlags.Public | BindingFlags.Instance) is not null,
                row.Name + " names InternalItemsQuery." + row.QueryProperty + ", which is not on the "
                + ServerLine().ToString(2) + " line the suite is compiled against.");
        }
    }

    /// <summary>
    /// A row narrowed by the query is not read after it, and a row read after the query names no
    /// property. One column carries both, so this reads the pair the way a caller does rather
    /// than the way the row stores it.
    /// </summary>
    [Fact]
    public void EveryRowIsEitherNarrowedByTheQueryOrReadAfterItAndNeverBoth()
    {
        foreach (var row in RuleFieldTable.Rows)
        {
            Assert.True(
                row.IsPostQuery ^ (row.QueryProperty is not null),
                row.Name + " is neither narrowed by the query nor read after it, or is both.");
        }
    }

    /// <summary>
    /// A field marked post-query because nobody looked is worse than one that is genuinely
    /// post-query, because the whole library is walked for it on every refresh. This asks the
    /// server query whether it carries a property under the field's own name.
    /// </summary>
    /// <remarks>
    /// The bound is the name. A property that narrows the same thing under a name this test
    /// cannot derive from the field is invisible to it, so this refuses the careless case and not
    /// every case. What it does refuse is the one that has actually happened elsewhere in this
    /// tree: a surface read once, written down, and never asked again.
    /// </remarks>
    [Fact]
    public void NoPostQueryRowIsPostQueryUnderANameTheServerQueryAlreadyCarries()
    {
        foreach (var row in RuleFieldTable.Rows)
        {
            if (!row.IsPostQuery)
            {
                continue;
            }

            var pascal = char.ToUpperInvariant(row.Name[0]) + row.Name[1..];

            Assert.True(
                typeof(InternalItemsQuery).GetProperty(pascal, BindingFlags.Public | BindingFlags.Instance) is null,
                row.Name + " is marked as read after the query and InternalItemsQuery carries " + pascal + ".");
        }
    }

    /// <summary>
    /// Both of the ways a field reaches the library are exercised by the table, so neither branch
    /// of the tests above is passing because no row takes it.
    /// </summary>
    [Fact]
    public void TheTableExercisesBothWaysAFieldReachesTheLibrary()
    {
        Assert.Contains(RuleFieldTable.Rows, row => row.IsPostQuery);
        Assert.Contains(RuleFieldTable.Rows, row => !row.IsPostQuery);
    }
}
