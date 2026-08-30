using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The written form of every value type lives in <c>docs/rule-values.md</c>, and a form that has
/// drifted from the parser that enforces it is worse than no page: somebody reads that a date may
/// be written without an offset, writes one, and meets a refusal the page says cannot happen.
/// These tests compare the page against the declared types in both directions, and compare each
/// sentence on it against the one a refusal shows the operator.
/// </summary>
public class RuleValueDocumentTests
{
    private const string Page = "docs/rule-values.md";

    /// <summary>
    /// A section on that page: the heading naming the type, then the sentence the parser refuses
    /// against. Both markers are literal, so prose that happens to mention a type is not a
    /// section and a section cannot lose its form line quietly.
    /// </summary>
    private static readonly Regex Section = new(
        @"^## Value type: (?<name>[A-Za-z]+)\r?\n\r?\n(?<intro>.*?)^Accepted form: (?<form>.+?)\r?$",
        RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static IReadOnlyDictionary<string, string> DocumentedForms()
        => Section.Matches(RepositoryFiles.ReadFromRoot(Page))
            .ToDictionary(
                section => section.Groups["name"].Value,
                section => section.Groups["form"].Value,
                StringComparer.Ordinal);

    private static IEnumerable<RuleValueType> DeclaredTypes()
        => Enum.GetValues<RuleValueType>();

    /// <summary>
    /// Without this the pair below passes on a page somebody emptied, because two empty sets
    /// agree and every documented form is then trivially correct.
    /// </summary>
    [Fact]
    public void ThePageCarriesASectionPerType()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryFiles.Root(), Page)), Page + " is not in the tree.");
        Assert.NotEmpty(DocumentedForms());
    }

    [Fact]
    public void EveryDeclaredTypeHasASectionAndEverySectionNamesADeclaredType()
    {
        Assert.Equal(
            DeclaredTypes().Select(type => type.ToString()).OrderBy(name => name, StringComparer.Ordinal),
            DocumentedForms().Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// The sentence on the page and the sentence in a refusal are one string rather than two
    /// wordings that agree today.
    /// </summary>
    [Fact]
    public void EverySectionCarriesTheFormTheRefusalShows()
    {
        var documented = DocumentedForms();

        foreach (var type in DeclaredTypes())
        {
            Assert.Equal(RuleValueForm.Of(type), documented[type.ToString()]);
        }
    }

    /// <summary>
    /// A type declared without a parser is a type nothing can read, and the compiler does not
    /// notice: the reader is looked up by the field's declared type at the point a condition is
    /// validated, and a missing one is a gap in a table rather than a call that fails to compile.
    /// </summary>
    [Fact]
    public void EveryDeclaredTypeHasAReaderNamedAfterIt()
    {
        var readers = typeof(RuleValueParser)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            DeclaredTypes().Select(type => "Read" + type).OrderBy(name => name, StringComparer.Ordinal),
            readers.Where(name => name.StartsWith("Read", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// The one arm of the form table no document reaches, reached by hand. A value type added to
    /// the enum without a sentence beside it comes out of the switch as this throw rather than as
    /// a refusal describing nothing.
    /// </summary>
    [Fact]
    public void AValueTypeWithNoDeclaredFormThrowsRatherThanDescribingNothing()
    {
        var undeclared = (RuleValueType)(-1);

        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => RuleValueForm.Of(undeclared));

        Assert.Equal("type", thrown.ParamName);
    }
}
