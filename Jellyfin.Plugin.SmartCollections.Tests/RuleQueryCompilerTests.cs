using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The compiler turns the conditions a rule wrote into the query the server's own item store
/// answers. What has to hold of it is that each pair narrows the one property it declares and
/// nothing else, that a condition the query cannot carry is handed back rather than dropped, and
/// that two conditions writing one property are refused rather than one replacing the other.
/// </summary>
/// <remarks>
/// Every assertion about "every other property" is taken by reflecting over the query type the
/// suite is compiled against rather than against a list of names, through
/// <see cref="QuerySnapshot"/>.
///
/// WHAT THE SNAPSHOT CANNOT SEE is written down where the snapshot is, in
/// <see cref="QuerySnapshot"/>. Of the two bounds it names, the one this file has to answer for is
/// the second: the compile table writes no property that has no getter, which is asserted below
/// rather than assumed.
/// </remarks>
public class RuleQueryCompilerTests
{
    private static readonly DateTimeOffset AnInstant = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The scope every compilation below is made against. A rule always declares one, so there is
    /// no call here without it, and taking both kinds keeps these tests about the conditions.
    /// </summary>
    private static readonly IReadOnlyList<RuleItemKindRow> BothKinds = RuleItemKindTable.Rows;

    /// <summary>
    /// The property the scope writes. Every accepted compilation moves it, so the assertions below
    /// name it rather than exempting it: a test that filtered it out would pass on a compiler that
    /// stopped bounding the query.
    /// </summary>
    private const string ScopeProperty = "IncludeItemTypes";

    /// <summary>
    /// The properties an accepted compilation moves, which is what the conditions wrote plus the
    /// scope, in the order <see cref="QuerySnapshot.Moved"/> returns them.
    /// </summary>
    private static string[] AndTheScope(params string[] properties)
        => properties
            .Append(ScopeProperty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private static RuleConditionValue Condition(
        RuleField field,
        RuleOperator @operator,
        string pointer,
        params RuleValue[] values)
        => new(pointer, RuleFieldTable.Of(field), RuleOperatorTable.Of(@operator), values);

    /// <summary>
    /// One value of the type a pair takes beside it. The type comes from the operator table rather
    /// than from the field, because that is the answer the value stage itself parses against.
    /// </summary>
    private static RuleValue Sample(RuleQueryRow row, int ordinal)
    {
        var type = RuleOperatorTable.ValueTypeFor(row.Operator, RuleFieldTable.Of(row.Field).ValueType);

        return type switch
        {
            RuleValueType.String => RuleValue.Of(type, "value" + ordinal.ToString(CultureInfo.InvariantCulture)),
            RuleValueType.Integer => RuleValue.Of(type, 1994L + ordinal),
            RuleValueType.Decimal => RuleValue.Of(type, 8.1m),
            RuleValueType.Date => RuleValue.Of(type, AnInstant),
            _ => throw new InvalidOperationException(
                "No sample value is written for " + type + ", which a compiled pair now takes.")
        };
    }

    private static RuleConditionValue Condition(RuleQueryRow row, string pointer)
    {
        var count = RuleOperatorTable.Of(row.Operator).TakesAList ? 2 : 1;
        var values = Enumerable.Range(0, count).Select(ordinal => Sample(row, ordinal)).ToArray();

        return Condition(row.Field, row.Operator, pointer, values);
    }

    /// <summary>
    /// The reading every test below rests on reads something. A snapshot that silently returned
    /// nothing would make every comparison here trivially true.
    /// </summary>
    [Fact]
    public void TheSnapshotReadsEveryPropertyTheCompileTableWrites()
    {
        var snapshot = QuerySnapshot.Of(new InternalItemsQuery());

        Assert.NotEmpty(snapshot);
        foreach (var row in RuleQueryTable.Rows)
        {
            Assert.True(snapshot.ContainsKey(row.QueryProperty), row.QueryProperty + " is not read.");
        }
    }

    /// <summary>
    /// The done condition this test carries: a test per row asserting the compiled query sets
    /// exactly the expected property and leaves every other property at its default.
    /// </summary>
    [Fact]
    public void EveryPairSetsExactlyThePropertyItNamesAndLeavesEveryOtherAtItsDefault()
    {
        foreach (var row in RuleQueryTable.Rows)
        {
            var compilation = RuleQueryCompiler.Compile(BothKinds, [Condition(row, "/match/allOf/0")]);

            Assert.True(compilation.IsAccepted);
            Assert.Empty(compilation.AfterTheQuery);
            Assert.Equal(AndTheScope(row.QueryProperty), QuerySnapshot.Moved(compilation.Query));
        }
    }

    /// <summary>
    /// The done condition this test carries, and the bound the post-query stage rests on: a rule
    /// whose conditions the query cannot express still asks the server for its declared kinds and
    /// never for the library. The scope is written whether or not one condition compiled.
    /// </summary>
    [Fact]
    public void NoConditionsCompileToAQueryNarrowedByTheScopeAndNothingElse()
    {
        var compilation = RuleQueryCompiler.Compile(BothKinds, []);

        Assert.True(compilation.IsAccepted);
        Assert.Empty(compilation.AfterTheQuery);
        Assert.Equal([ScopeProperty], QuerySnapshot.Moved(compilation.Query));
    }

    /// <summary>
    /// The other half of the done condition's last clause: the query names what it collects and
    /// never what it does not, so the exclusion list is left where the server's constructor leaves
    /// it.
    /// </summary>
    [Fact]
    public void TheScopeIsCompiledToExactlyTheDeclaredKindsAndExcludesNothing()
    {
        foreach (var scope in new[] { BothKinds, new[] { RuleItemKindTable.Of(RuleItemKind.Series) } })
        {
            var compilation = RuleQueryCompiler.Compile(scope, []);

            Assert.True(compilation.IsAccepted);
            Assert.Equal(
                scope.Select(row => row.ServerKind),
                compilation.Query.IncludeItemTypes);
            Assert.Empty(compilation.Query.ExcludeItemTypes);
        }
    }

    /// <summary>
    /// A scope is a set and the reader hands it over in the table's order, so the query a rule
    /// compiles to does not depend on the order a document wrote its kinds in.
    /// </summary>
    [Fact]
    public void TheScopeIsWrittenInTheTablesOrder()
    {
        var compilation = RuleQueryCompiler.Compile(BothKinds, []);

        Assert.Equal(
            RuleItemKindTable.Rows.Select(row => row.ServerKind),
            compilation.Query.IncludeItemTypes);
    }

    /// <summary>
    /// An empty scope would compile to an include list the server reads as no narrowing at all,
    /// which is the one shape this stage may not produce. The scope reader cannot hand one over,
    /// so this is a fault in a caller that built a scope some other way.
    /// </summary>
    [Fact]
    public void AnEmptyScopeIsRefusedRatherThanCompiledToAnUnboundedQuery()
        => Assert.Throws<ArgumentException>(() => RuleQueryCompiler.Compile([], []));

    [Fact]
    public void ConditionsOverDifferentPropertiesAreAllWritten()
    {
        var compilation = RuleQueryCompiler.Compile(BothKinds, 
        [
            Condition(RuleField.Genres, RuleOperator.Contains, "/match/allOf/0", RuleValue.Of(RuleValueType.String, "Thriller")),
            Condition(RuleField.ProductionYear, RuleOperator.Equals, "/match/allOf/1", RuleValue.Of(RuleValueType.Integer, 1994L))
        ]);

        Assert.True(compilation.IsAccepted);
        Assert.Equal(AndTheScope("Genres", "Years"), QuerySnapshot.Moved(compilation.Query));
    }

    /// <summary>
    /// Two conditions on one field that write two different properties are the case the refusal
    /// below is deliberately narrower than, and it is a rule somebody would write.
    /// </summary>
    [Fact]
    public void TwoConditionsOnOneFieldWritingDifferentPropertiesAreBothCompiled()
    {
        var compilation = RuleQueryCompiler.Compile(BothKinds, 
        [
            Condition(RuleField.Tags, RuleOperator.Contains, "/match/allOf/0", RuleValue.Of(RuleValueType.String, "keep")),
            Condition(RuleField.Tags, RuleOperator.NotContains, "/match/allOf/1", RuleValue.Of(RuleValueType.String, "drop"))
        ]);

        Assert.True(compilation.IsAccepted);
        Assert.Equal(AndTheScope("ExcludeTags", "Tags"), QuerySnapshot.Moved(compilation.Query));
    }

    /// <summary>
    /// The done condition this test carries: a rule with two conditions on the same field is
    /// refused rather than the second silently overwriting the first.
    /// </summary>
    [Fact]
    public void TwoConditionsWritingOneQueryPropertyAreRefusedNamingBoth()
    {
        var compilation = RuleQueryCompiler.Compile(BothKinds, 
        [
            Condition(RuleField.ProductionYear, RuleOperator.Equals, "/match/allOf/0", RuleValue.Of(RuleValueType.Integer, 1994L)),
            Condition(RuleField.ProductionYear, RuleOperator.In, "/match/allOf/1", RuleValue.Of(RuleValueType.Integer, 1995L))
        ]);

        Assert.False(compilation.IsAccepted);
        var error = Assert.Single(compilation.Errors);
        Assert.Equal("/match/allOf/1", error.Pointer);
        Assert.Equal(
            "The condition at \"/match/allOf/0\" already narrows the query on \"productionYear\". "
            + "Both conditions write Years, and the query holds one value for it.",
            error.Message);
    }

    /// <summary>
    /// A refused compilation hands back a query nobody can mistake for an answer. A query carrying
    /// the first of two conditions selects a superset of what the rule means, and a caller reading
    /// only the query would ship it.
    /// </summary>
    [Fact]
    public void ARefusedCompilationNarrowsNothing()
    {
        var compilation = RuleQueryCompiler.Compile(BothKinds, 
        [
            Condition(RuleField.Tags, RuleOperator.Contains, "/match/allOf/0", RuleValue.Of(RuleValueType.String, "keep")),
            Condition(RuleField.Tags, RuleOperator.Contains, "/match/allOf/1", RuleValue.Of(RuleValueType.String, "also"))
        ]);

        Assert.False(compilation.IsAccepted);
        Assert.Empty(compilation.AfterTheQuery);
        Assert.Empty(QuerySnapshot.Moved(compilation.Query));
    }

    [Fact]
    public void AConditionThePairTableDoesNotCompileIsHandedBackRatherThanDropped()
    {
        var condition = Condition(
            RuleField.Name,
            RuleOperator.EndsWith,
            "/match/allOf/0",
            RuleValue.Of(RuleValueType.String, "Part II"));

        var compilation = RuleQueryCompiler.Compile(BothKinds, [condition]);

        Assert.True(compilation.IsAccepted);
        Assert.Equal([ScopeProperty], QuerySnapshot.Moved(compilation.Query));
        Assert.Same(condition, Assert.Single(compilation.AfterTheQuery));
    }

    /// <summary>
    /// A field with no query property at all takes the same route, and it is the route the whole
    /// post-query stage is reached by.
    /// </summary>
    [Fact]
    public void AConditionOnAFieldReadAfterTheQueryIsHandedBack()
    {
        var condition = Condition(
            RuleField.Overview,
            RuleOperator.Contains,
            "/match/allOf/0",
            RuleValue.Of(RuleValueType.String, "heist"));

        var compilation = RuleQueryCompiler.Compile(BothKinds, [condition]);

        Assert.True(compilation.IsAccepted);
        Assert.Equal([ScopeProperty], QuerySnapshot.Moved(compilation.Query));
        Assert.Same(condition, Assert.Single(compilation.AfterTheQuery));
    }

    [Fact]
    public void TheConditionsHandedBackKeepTheOrderTheDocumentWroteThem()
    {
        var first = Condition(RuleField.Name, RuleOperator.EndsWith, "/match/allOf/0", RuleValue.Of(RuleValueType.String, "Part II"));
        var second = Condition(RuleField.Overview, RuleOperator.Contains, "/match/allOf/1", RuleValue.Of(RuleValueType.String, "heist"));

        var compilation = RuleQueryCompiler.Compile(BothKinds, [first, second]);

        Assert.Equal([first, second], compilation.AfterTheQuery);
    }

    /// <summary>
    /// The two boundary instants are documents this plugin accepts and these properties cannot
    /// carry, because the offset that turns an at-or-after comparison into an after one has no
    /// room at the end of the range.
    /// </summary>
    [Fact]
    public void AnInstantWithNoRoomForTheOffsetIsHandedBackRatherThanNarrowed()
    {
        var last = Condition(
            RuleField.PremiereDate,
            RuleOperator.After,
            "/match/allOf/0",
            RuleValue.Of(RuleValueType.Date, new DateTimeOffset(DateTime.MaxValue, TimeSpan.Zero)));

        var first = Condition(
            RuleField.PremiereDate,
            RuleOperator.Before,
            "/match/allOf/0",
            RuleValue.Of(RuleValueType.Date, new DateTimeOffset(DateTime.MinValue, TimeSpan.Zero)));

        foreach (var condition in new[] { last, first })
        {
            var compilation = RuleQueryCompiler.Compile(BothKinds, [condition]);

            Assert.True(compilation.IsAccepted);
            Assert.Equal([ScopeProperty], QuerySnapshot.Moved(compilation.Query));
            Assert.Same(condition, Assert.Single(compilation.AfterTheQuery));
        }
    }

    /// <summary>
    /// The offset is what makes an at-or-after property mean the operator's own strictly-later
    /// sentence, so the instant the query carries is one tick past the one the document wrote.
    /// </summary>
    [Fact]
    public void AfterAndBeforeWriteTheInstantTheOperatorMeans()
    {
        var after = RuleQueryCompiler.Compile(BothKinds, 
        [
            Condition(RuleField.PremiereDate, RuleOperator.After, "/match/allOf/0", RuleValue.Of(RuleValueType.Date, AnInstant))
        ]);

        var before = RuleQueryCompiler.Compile(BothKinds, 
        [
            Condition(RuleField.PremiereDate, RuleOperator.Before, "/match/allOf/0", RuleValue.Of(RuleValueType.Date, AnInstant))
        ]);

        Assert.Equal(AnInstant.UtcDateTime.AddTicks(1), after.Query.MinPremiereDate);
        Assert.Equal(AnInstant.UtcDateTime.AddTicks(-1), before.Query.MaxPremiereDate);
    }

    /// <summary>
    /// A production year outside the range the query's own year array holds is the other document
    /// this plugin accepts and a property cannot carry. Both ends of that range are documents
    /// somebody can write, so both are asserted rather than the one that is easier to reach.
    /// </summary>
    [Fact]
    public void AYearTheQueryCannotHoldIsHandedBackRatherThanNarrowed()
    {
        var beyond = new[] { (long)int.MaxValue + 1, (long)int.MinValue - 1 };

        foreach (var year in beyond)
        {
            var condition = Condition(
                RuleField.ProductionYear,
                RuleOperator.In,
                "/match/allOf/0",
                RuleValue.Of(RuleValueType.Integer, 1994L),
                RuleValue.Of(RuleValueType.Integer, year));

            var compilation = RuleQueryCompiler.Compile(BothKinds, [condition]);

            Assert.True(compilation.IsAccepted);
            Assert.Equal([ScopeProperty], QuerySnapshot.Moved(compilation.Query));
            Assert.Same(condition, Assert.Single(compilation.AfterTheQuery));
        }
    }

    /// <summary>
    /// A pair handed back writes nothing, so the property it would have written is still free for
    /// a later condition. Refusing there would refuse a document nothing had narrowed.
    /// </summary>
    [Fact]
    public void APairHandedBackDoesNotClaimThePropertyItWouldHaveWritten()
    {
        var compilation = RuleQueryCompiler.Compile(BothKinds, 
        [
            Condition(
                RuleField.ProductionYear,
                RuleOperator.Equals,
                "/match/allOf/0",
                RuleValue.Of(RuleValueType.Integer, (long)int.MaxValue + 1)),
            Condition(
                RuleField.ProductionYear,
                RuleOperator.Equals,
                "/match/allOf/1",
                RuleValue.Of(RuleValueType.Integer, 1994L))
        ]);

        Assert.True(compilation.IsAccepted);
        Assert.Equal(AndTheScope("Years"), QuerySnapshot.Moved(compilation.Query));
    }

    /// <summary>
    /// One condition per property the table writes, which is every pair the table declares with
    /// the pairs that share a property taken once. Taking them all would be refused rather than
    /// compiled, and two refusals agree without either having narrowed anything.
    /// </summary>
    [Fact]
    public void CompilingTheSameConditionsTwiceProducesTheSameQuery()
    {
        var conditions = RuleQueryTable.Rows
            .GroupBy(row => row.QueryProperty, StringComparer.Ordinal)
            .Select((group, ordinal) => Condition(group.First(), "/match/allOf/" + ordinal.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        Assert.True(RuleQueryCompiler.Compile(BothKinds, conditions).IsAccepted);

        Assert.Equal(
            QuerySnapshot.Of(RuleQueryCompiler.Compile(BothKinds, conditions).Query),
            QuerySnapshot.Of(RuleQueryCompiler.Compile(BothKinds, conditions).Query));
    }

    [Fact]
    public void CompilingNothingAtAllIsRefusedRatherThanIgnored()
    {
        Assert.Throws<ArgumentNullException>(() => RuleQueryCompiler.Compile(BothKinds, null!));
        Assert.Throws<ArgumentNullException>(() => RuleQueryCompiler.Compile(null!, []));
    }
}
