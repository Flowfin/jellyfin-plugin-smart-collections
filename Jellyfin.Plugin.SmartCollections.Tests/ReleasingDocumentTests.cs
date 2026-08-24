using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// <c>docs/RELEASING.md</c> is followed by somebody cutting a release, which is the one route
/// where being wrong costs a tag. It tells that person where the version is written and which
/// tests refuse a partial edit, and both of those are statements about the tree that go stale
/// without anything noticing. These tests hold the page to them.
/// </summary>
public class ReleasingDocumentTests
{
    /// <summary>
    /// The page a releaser follows.
    /// </summary>
    private const string Page = "docs/RELEASING.md";

    /// <summary>
    /// The file the assemblies take their version from. The manifests are not listed beside it,
    /// because the suite already derives those from the tree.
    /// </summary>
    private const string PropsFile = "Directory.Build.props";

    /// <summary>
    /// A test named in the page, written as the class and the method inside a code span. Reading
    /// the names out of the page rather than listing them here is the whole point: a test renamed
    /// without the page moving is what this refuses.
    /// </summary>
    private static readonly Regex NamedTest = new(
        "`(?<type>[A-Za-z][A-Za-z0-9]*Tests)[.](?<method>[A-Za-z][A-Za-z0-9]*)`",
        RegexOptions.CultureInvariant);

    [Fact]
    public void EveryTestTheReleasingPageNamesExists()
    {
        var page = RepositoryFiles.ReadFromRoot(Page);
        var named = NamedTest.Matches(page);

        Assert.True(
            named.Count > 0,
            Page + " names no test, so this holds nothing. Either the page stopped citing the "
                + "guards that refuse a partial version edit, or the spelling it cites them in changed.");

        foreach (Match match in named)
        {
            var typeName = match.Groups["type"].Value;
            var methodName = match.Groups["method"].Value;

            var type = Assembly.GetExecutingAssembly().GetTypes()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, typeName, StringComparison.Ordinal));

            Assert.True(type is not null, Page + " names " + typeName + ", which is not a test class in this assembly.");

            Assert.True(
                type!.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance) is not null,
                Page + " names " + typeName + "." + methodName + ", which " + typeName + " does not declare.");
        }
    }

    [Fact]
    public void TheReleasingPageNamesEveryPlaceTheVersionIsWritten()
    {
        var page = RepositoryFiles.ReadFromRoot(Page);

        // Derived rather than listed, so a manifest added for a third server line is a file the
        // releaser is told about the moment it exists rather than one they find out about by
        // pushing a tag.
        foreach (var place in RepositoryFiles.ManifestNames().Append(PropsFile))
        {
            Assert.True(
                page.Contains(place, StringComparison.Ordinal),
                Page + " does not name " + place + ", which carries the version a release ships. "
                    + "A releaser following that page raises the number somewhere else and leaves this one behind.");
        }
    }
}
