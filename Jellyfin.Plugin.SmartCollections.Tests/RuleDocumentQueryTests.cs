using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A document walked through every stage that reads it, and the query that comes out. What is held
/// here is the property the composition stage declines to claim for itself: the tree preserves the
/// order a document wrote its siblings in, and the compiled form does not depend on that order.
/// </summary>
/// <remarks>
/// The four stages are called in the order a document meets them, rather than through a validator,
/// because none of them is wired into one yet. That is the same route the stages' own suites take,
/// so this file adds a walk rather than a second way of reading a document.
///
/// WHAT THE COMPARISON IS OVER is the query. Two documents that differ only in the order of
/// siblings do NOT produce the same conditions in the same order - the pointers move with the
/// members, which is what makes a refusal point at the right place - and the query they compile to
/// is the same. Asserting the queries rather than the reads is therefore the assertion, not a
/// weaker stand-in for one.
/// </remarks>
public class RuleDocumentQueryTests
{
    private const string ThreeConditions = """
        {
          "schemaVersion": 1,
          "id": "thrillers-of-1994",
          "name": "Thrillers of 1994",
          "match": {
            "allOf": [
              { "field": "genres", "operator": "contains", "value": "Thriller" },
              { "field": "productionYear", "operator": "equals", "value": 1994 },
              { "field": "tags", "operator": "contains", "value": "keep" }
            ]
          }
        }
        """;

    private const string TheSameThreeReordered = """
        {
          "schemaVersion": 1,
          "id": "thrillers-of-1994",
          "name": "Thrillers of 1994",
          "match": {
            "allOf": [
              { "field": "tags", "operator": "contains", "value": "keep" },
              { "field": "genres", "operator": "contains", "value": "Thriller" },
              { "field": "productionYear", "operator": "equals", "value": 1994 }
            ]
          }
        }
        """;

    /// <summary>
    /// A document whose siblings are nested rather than listed side by side, so the reordering
    /// below is not the only shape the property is asserted over.
    /// </summary>
    private const string ThreeConditionsNested = """
        {
          "schemaVersion": 1,
          "id": "thrillers-of-1994",
          "name": "Thrillers of 1994",
          "match": {
            "allOf": [
              { "field": "tags", "operator": "contains", "value": "keep" },
              {
                "allOf": [
                  { "field": "productionYear", "operator": "equals", "value": 1994 },
                  { "field": "genres", "operator": "contains", "value": "Thriller" }
                ]
              }
            ]
          }
        }
        """;

    private static RuleValueRead ReadRule(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var composition = RuleCompositionReader.Read(root.GetProperty("match"), "/match");
        Assert.True(composition.IsAccepted, string.Join("; ", composition.Errors.Select(error => error.ToString())));

        var fields = RuleFieldReader.Read(root, composition.Group!);
        Assert.True(fields.IsAccepted, string.Join("; ", fields.Errors.Select(error => error.ToString())));

        var operators = RuleOperatorReader.Read(root, fields.Fields);
        Assert.True(operators.IsAccepted, string.Join("; ", operators.Errors.Select(error => error.ToString())));

        var values = RuleValueReader.Read(root, operators.Operators);
        Assert.True(values.IsAccepted, string.Join("; ", values.Errors.Select(error => error.ToString())));

        return values;
    }

    private static InternalItemsQuery Compile(string json)
    {
        var compilation = RuleQueryCompiler.Compile(ReadRule(json).Conditions);

        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Errors.Select(error => error.ToString())));
        Assert.Empty(compilation.AfterTheQuery);

        return compilation.Query;
    }

    /// <summary>
    /// Without this the comparison below could pass on a walk that read no condition at all, and a
    /// query narrowed by nothing equals another query narrowed by nothing.
    /// </summary>
    [Fact]
    public void TheWalkReadsEveryConditionTheDocumentWrote()
    {
        Assert.Equal(3, ReadRule(ThreeConditions).Conditions.Count);
        Assert.Equal(
            ["Genres", "Tags", "Years"],
            QuerySnapshot.Moved(Compile(ThreeConditions)));
    }

    /// <summary>
    /// The order is genuinely different rather than the same document twice, which is the other way
    /// the comparison below could pass without asserting anything.
    /// </summary>
    [Fact]
    public void TheTwoDocumentsWriteTheirSiblingsInDifferentOrders()
    {
        static IReadOnlyList<string> Fields(string json)
            => ReadRule(json).Conditions.Select(condition => condition.Field.Name).ToList();

        Assert.Equal(["genres", "productionYear", "tags"], Fields(ThreeConditions));
        Assert.Equal(["tags", "genres", "productionYear"], Fields(TheSameThreeReordered));
        Assert.NotEqual(Fields(ThreeConditions), Fields(TheSameThreeReordered));
    }

    /// <summary>
    /// The done condition this test carries: a document and the same document with sibling
    /// conditions reordered compile to the same query.
    /// </summary>
    [Fact]
    public void ADocumentAndTheSameDocumentWithSiblingsReorderedCompileToTheSameQuery()
    {
        Assert.Equal(
            QuerySnapshot.Of(Compile(ThreeConditions)),
            QuerySnapshot.Of(Compile(TheSameThreeReordered)));
    }

    /// <summary>
    /// The same three conditions with two of them moved into a nested group of the same kind. The
    /// tree is a different shape and the conjunction it states is the same one, so the query is
    /// the same query.
    /// </summary>
    [Fact]
    public void ADocumentThatNestsTheSameConjunctionCompilesToTheSameQuery()
    {
        Assert.Equal(
            QuerySnapshot.Of(Compile(ThreeConditions)),
            QuerySnapshot.Of(Compile(ThreeConditionsNested)));
    }

    /// <summary>
    /// The pointers DO move with the members, which is what makes a refusal point at the place the
    /// operator has to repair. That is the half the assertion above is deliberately not over, so it
    /// is asserted rather than left as a sentence.
    /// </summary>
    [Fact]
    public void ThePointersMoveWithTheMembersEvenThoughTheQueryDoesNot()
    {
        static IReadOnlyList<string> PointersOfGenres(string json)
            => ReadRule(json).Conditions
                .Where(condition => condition.Field.Name == "genres")
                .Select(condition => condition.Pointer)
                .ToList();

        Assert.Equal(["/match/allOf/0"], PointersOfGenres(ThreeConditions));
        Assert.Equal(["/match/allOf/1"], PointersOfGenres(TheSameThreeReordered));
    }
}
