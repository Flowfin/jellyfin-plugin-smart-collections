using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A rule read on a server running in Turkish has to compile to the query it compiles to on a
/// server running in English, and a rule refused on one has to be refused on the other with the
/// same words. Culture-sensitive comparison and culture-sensitive parsing are the classic way a
/// matching engine stops being reproducible, and neither shows up on a machine whose locale is the
/// one the code was written on.
/// </summary>
/// <remarks>
/// The three cultures are the ones the determinism milestone names. Turkish is there because the
/// uppercase of <c>i</c> is not <c>I</c>, so a folded comparison answers differently for a field
/// name, an operator name and a kind name; Arabic (Saudi Arabia) is there because its default
/// calendar is not the Gregorian one and its digits are not the ASCII ones, so a date or a number
/// parsed against the ambient culture is not the value the document wrote.
///
/// WHAT THIS DOES NOT COVER is the clause #25 leads with, which is about comparisons an evaluation
/// makes against library values. Nothing here reads an item, because nothing evaluates one yet.
/// What is asserted is the whole path a document takes today: the five stages that read it and the
/// query they compile to.
///
/// The culture is set on the running thread and put back in a <c>finally</c>, so this needs no
/// display, no elevated rights and no machine trust store.
/// </remarks>
public class RuleReadUnderAServerLocaleTests
{
    /// <summary>
    /// A rule whose members reach every stage: a group, three fields of three different value
    /// types, an operator that takes a list, a date, a decimal and a year.
    /// </summary>
    private const string Rule = """
        {
          "schemaVersion": 1,
          "id": "nineties-thrillers",
          "name": "Nineties Thrillers",
          "collects": ["movie", "series"],
          "match": {
            "allOf": [
              { "field": "genres", "operator": "contains", "value": "Sci-Fi" },
              { "field": "productionYear", "operator": "in", "value": [1994, 1995] },
              { "field": "communityRating", "operator": "greaterThanOrEqual", "value": 8.1 },
              { "field": "premiereDate", "operator": "after", "value": "1994-01-01T00:00:00Z" }
            ]
          }
        }
        """;

    /// <summary>
    /// A rule every stage refuses something in, so the messages can be compared as well as the
    /// query: an unknown kind, an unknown field, an operator the field does not accept and a value
    /// that will not parse.
    /// </summary>
    private const string RefusedRule = """
        {
          "schemaVersion": 1,
          "id": "broken",
          "name": "Broken",
          "collects": ["episode"],
          "match": {
            "allOf": [
              { "field": "titel", "operator": "contains", "value": "x" },
              { "field": "productionYear", "operator": "equals", "value": "1994" }
            ]
          }
        }
        """;

    private static readonly string[] Locales = ["", "tr-TR", "ar-SA"];

    private static T UnderLocale<T>(string locale, Func<T> read)
    {
        var culture = CultureInfo.GetCultureInfo(locale);
        var wasCulture = CultureInfo.CurrentCulture;
        var wasUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return read();
        }
        finally
        {
            CultureInfo.CurrentCulture = wasCulture;
            CultureInfo.CurrentUICulture = wasUiCulture;
        }
    }

    /// <summary>
    /// The five stages in the order a document meets them, which is the same order
    /// <see cref="RuleDocumentQueryTests"/> walks them in.
    /// </summary>
    private static (bool Accepted, IReadOnlyList<string> Errors, IReadOnlyDictionary<string, string> Query) Walk(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        var envelope = RuleDocumentValidator.Read(json);
        if (!envelope.IsValid)
        {
            return (false, Rendered(envelope.Errors), QuerySnapshot.Of(new InternalItemsQuery()));
        }

        var scope = RuleItemScopeReader.Read(root);
        if (!scope.IsAccepted)
        {
            return (false, Rendered(scope.Errors), QuerySnapshot.Of(new InternalItemsQuery()));
        }

        var composition = RuleCompositionReader.Read(root.GetProperty("match"), "/match");
        if (!composition.IsAccepted)
        {
            return (false, Rendered(composition.Errors), QuerySnapshot.Of(new InternalItemsQuery()));
        }

        var fields = RuleFieldReader.Read(root, composition.Group!);
        if (!fields.IsAccepted)
        {
            return (false, Rendered(fields.Errors), QuerySnapshot.Of(new InternalItemsQuery()));
        }

        var operators = RuleOperatorReader.Read(root, fields.Fields);
        if (!operators.IsAccepted)
        {
            return (false, Rendered(operators.Errors), QuerySnapshot.Of(new InternalItemsQuery()));
        }

        var values = RuleValueReader.Read(root, operators.Operators);
        if (!values.IsAccepted)
        {
            return (false, Rendered(values.Errors), QuerySnapshot.Of(new InternalItemsQuery()));
        }

        var compilation = RuleQueryCompiler.Compile(scope.Kinds, values.Conditions);

        return (
            compilation.IsAccepted,
            Rendered(compilation.Errors),
            QuerySnapshot.Of(compilation.Query));
    }

    private static string[] Rendered(IEnumerable<RuleValidationError> errors)
        => errors.Select(error => error.ToString()).ToArray();

    /// <summary>
    /// Without this the comparisons below could pass on a walk that refused the document at its
    /// first stage under every culture, which agrees with itself and asserts nothing about the
    /// stages behind it.
    /// </summary>
    [Fact]
    public void TheRuleThisFileWalksIsOneEveryStageAccepts()
    {
        var walked = Walk(Rule);

        Assert.True(walked.Accepted, "Refused with: " + string.Join(" | ", walked.Errors));
        Assert.Equal(
            ["Genres", "IncludeItemTypes", "MinCommunityRating", "MinPremiereDate", "Years"],
            QuerySnapshot.Moved(Compiled(Rule)));
    }

    private static InternalItemsQuery Compiled(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        var scope = RuleItemScopeReader.Read(root);
        var composition = RuleCompositionReader.Read(root.GetProperty("match"), "/match");
        var fields = RuleFieldReader.Read(root, composition.Group!);
        var operators = RuleOperatorReader.Read(root, fields.Fields);
        var values = RuleValueReader.Read(root, operators.Operators);

        return RuleQueryCompiler.Compile(scope.Kinds, values.Conditions).Query;
    }

    /// <summary>
    /// The query is the artefact an evaluation is made from, so it is what has to be identical.
    /// Comparing the reads rather than the query would pass on two reads that agree and compile
    /// differently.
    /// </summary>
    [Fact]
    public void ARuleCompilesToOneQueryWhateverLocaleTheServerRunsIn()
    {
        var reference = UnderLocale(Locales[0], () => Walk(Rule));

        Assert.True(reference.Accepted, "Refused with: " + string.Join(" | ", reference.Errors));

        foreach (var locale in Locales.Skip(1))
        {
            var walked = UnderLocale(locale, () => Walk(Rule));

            Assert.True(walked.Accepted, locale + " refused with: " + string.Join(" | ", walked.Errors));
            Assert.Equal(reference.Query, walked.Query);
        }
    }

    /// <summary>
    /// A refusal is what an operator reads, and it carries names and numbers. A message assembled
    /// against the ambient culture reaches them in a different form on a server whose locale is not
    /// the one the fixture was written on, which is the same defect one register along.
    /// </summary>
    [Fact]
    public void ARefusedRuleIsRefusedWithTheSameWordsWhateverLocaleTheServerRunsIn()
    {
        var reference = UnderLocale(Locales[0], () => Walk(RefusedRule));

        Assert.False(reference.Accepted);
        Assert.NotEmpty(reference.Errors);

        foreach (var locale in Locales.Skip(1))
        {
            var walked = UnderLocale(locale, () => Walk(RefusedRule));

            Assert.False(walked.Accepted, locale + " accepted a rule the invariant read refused.");
            Assert.Equal(reference.Errors, walked.Errors);
        }
    }

    /// <summary>
    /// The names a document writes are wire tokens, so a lookup that folded them would answer
    /// differently in Turkish: the uppercase of <c>i</c> is <c>I</c> with a dot there, so
    /// <c>PREMIEREDATE</c> folds onto <c>premıeredate</c> rather than onto <c>premiereDate</c>.
    /// Each table is asked directly, because a walk would report the same refusal for a name that
    /// resolved wrongly and for one that did not resolve at all.
    /// </summary>
    [Fact]
    public void NoTableResolvesANameItDoesNotDeclareUnderAnyLocale()
    {
        foreach (var locale in Locales)
        {
            UnderLocale(locale, () =>
            {
                foreach (var row in RuleFieldTable.Rows)
                {
                    Assert.Same(row, RuleFieldTable.Find(row.Name));
                    Assert.Null(RuleFieldTable.Find(row.Name.ToUpperInvariant()));
                }

                foreach (var row in RuleOperatorTable.Rows)
                {
                    Assert.Same(row, RuleOperatorTable.Find(row.Name));
                    Assert.Null(RuleOperatorTable.Find(row.Name.ToUpperInvariant()));
                }

                foreach (var row in RuleItemKindTable.Rows)
                {
                    Assert.Same(row, RuleItemKindTable.Find(row.Name));
                    Assert.Null(RuleItemKindTable.Find(row.Name.ToUpperInvariant()));
                }

                return true;
            });
        }
    }

    /// <summary>
    /// A date and a decimal are where an ambient parse stops being a translation and starts being
    /// a different value: the Saudi locale's default calendar is not the Gregorian one, and its
    /// decimal separator is not the point the document writes.
    /// </summary>
    [Fact]
    public void ADateAndADecimalParseToTheSameValueUnderAnyLocale()
    {
        var expected = UnderLocale(Locales[0], () => Compiled(Rule));

        foreach (var locale in Locales.Skip(1))
        {
            var query = UnderLocale(locale, () => Compiled(Rule));

            Assert.True(
                expected.MinPremiereDate == query.MinPremiereDate,
                locale + " read the date as " + query.MinPremiereDate);
            Assert.Equal(expected.MinCommunityRating, query.MinCommunityRating);
            Assert.Equal(expected.Years, query.Years);
        }
    }

    /// <summary>
    /// The locales this file names are ones the machine running the suite actually has, so a
    /// culture that silently fell back to the invariant one would make every comparison above
    /// compare the invariant read against itself.
    /// </summary>
    [Fact]
    public void TheLocalesThisFileNamesAreDistinctOnTheMachineRunningIt()
    {
        var separators = Locales
            .Select(locale => CultureInfo.GetCultureInfo(locale).NumberFormat.NumberDecimalSeparator)
            .ToArray();

        Assert.Equal("tr-TR", CultureInfo.GetCultureInfo("tr-TR").Name);
        Assert.Equal("ar-SA", CultureInfo.GetCultureInfo("ar-SA").Name);
        Assert.NotEqual(separators[0], separators[1]);
    }
}
