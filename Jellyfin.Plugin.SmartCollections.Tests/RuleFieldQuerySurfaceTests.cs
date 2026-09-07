using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A compiled pair that names a query property is a promise that the property is there on every
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
    /// The done condition this test carries: every property a compiled pair writes is present on
    /// the server query this leg is compiled against.
    /// </summary>
    /// <remarks>
    /// THIS USED TO READ THE FIELD TABLE, and the column it read is gone. A field row named one
    /// query property, which was the mark #31 moved onto the field and operator pair on
    /// 2026-09-04, and the properties are declared one per compiled pair now. The reading is the
    /// same reading against the same type; what moved is which table it is taken from.
    /// </remarks>
    [Fact]
    public void EveryPropertyACompiledPairWritesIsOnTheServerQuery()
    {
        foreach (var row in RuleQueryTable.Rows)
        {
            foreach (var property in row.QueryProperties)
            {
                Assert.True(
                    typeof(InternalItemsQuery).GetProperty(property, BindingFlags.Public | BindingFlags.Instance) is not null,
                    RuleFieldTable.Of(row.Field).Name + " " + RuleOperatorTable.Of(row.Operator).Name
                    + " writes InternalItemsQuery." + property + ", which is not on the "
                    + ServerLine().ToString(2) + " line the suite is compiled against.");
            }
        }
    }

    /// <summary>
    /// A pair answered after the query because nobody looked is worse than one that is genuinely
    /// answered there, because the whole scope is walked for it on every refresh. This asks the
    /// server query whether it carries a property under the field's own name.
    /// </summary>
    /// <remarks>
    /// The bound is the name, and it is a weaker bound than it was. A property that narrows the
    /// same thing under a name this test cannot derive from the field is invisible to it; so is a
    /// property that would answer one OPERATOR over a field the query already narrows on under a
    /// different name, which is the case the pair mark makes expressible and this test does not
    /// reach. What it refuses is the careless case: a field nothing on the query is asked about at
    /// all, carrying a property named after it.
    /// </remarks>
    [Fact]
    public void NoFieldIsLeftEntirelyToTheStageUnderANameTheServerQueryAlreadyCarries()
    {
        foreach (var row in RuleFieldTable.Rows)
        {
            if (RuleQueryTable.Narrows(row.Field))
            {
                continue;
            }

            var pascal = char.ToUpperInvariant(row.Name[0]) + row.Name[1..];

            Assert.True(
                typeof(InternalItemsQuery).GetProperty(pascal, BindingFlags.Public | BindingFlags.Instance) is null,
                row.Name + " is answered entirely after the query and InternalItemsQuery carries " + pascal + ".");
        }
    }

    /// <summary>
    /// Both answers the mark can give are exercised by the vocabulary, so neither branch of the
    /// tests above is passing because no pair takes it.
    /// </summary>
    [Fact]
    public void TheVocabularyExercisesBothAnswersTheMarkCanGive()
    {
        var pairs = RuleFieldTable.Rows
            .SelectMany(field => field.Operators.Select(@operator => (field.Field, Operator: @operator)))
            .ToArray();

        Assert.Contains(pairs, pair => RuleQueryTable.AnswersInTheQuery(pair.Field, pair.Operator));
        Assert.Contains(pairs, pair => !RuleQueryTable.AnswersInTheQuery(pair.Field, pair.Operator));
    }
}
