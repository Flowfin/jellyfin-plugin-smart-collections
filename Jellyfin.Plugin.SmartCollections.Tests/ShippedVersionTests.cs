using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The version a release carries is written in two kinds of file that nothing builds from each
/// other. The manifests at the repository root declare it to the catalogue, and
/// <c>Directory.Build.props</c> stamps it into the assemblies. The release route compares the
/// two and refuses to publish when they differ, which is the right refusal in the wrong place:
/// a tag is already spent by the time that step runs, and an immutable release burns its tag
/// permanently. These tests move the comparison to where a push can still be corrected.
/// </summary>
public class ShippedVersionTests
{
    /// <summary>
    /// The file the assemblies take their version from.
    /// </summary>
    private const string PropsFile = "Directory.Build.props";

    /// <summary>
    /// The three properties that decide what a shipped assembly says about itself.
    /// <c>AssemblyVersion</c> is the one the release route reads; the other two travel with it
    /// and a release naming three different numbers is worse than one naming two.
    /// </summary>
    private static readonly string[] VersionProperties =
        ["Version", "AssemblyVersion", "FileVersion"];

    private static readonly Regex ManifestVersion = new(
        "^version:[ \t]*\"?(?<value>[^\"\r\n]*?)\"?[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// A four-part number, which is what a Jellyfin manifest version and an
    /// <c>AssemblyVersion</c> are both written as here.
    /// </summary>
    private static readonly Regex FourParts = new(
        "^[0-9]+[.][0-9]+[.][0-9]+[.][0-9]+$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the version an MSBuild property is set to, from the file as text rather than by
    /// evaluating the project, so the test needs no build and reports the line somebody edits.
    /// </summary>
    /// <param name="property">The property name.</param>
    /// <returns>The value the property is set to.</returns>
    private static string PropertyValue(string property)
    {
        var match = new Regex(
            "<" + property + ">(?<value>[^<]*)</" + property + ">",
            RegexOptions.CultureInvariant).Match(RepositoryFiles.ReadFromRoot(PropsFile));

        Assert.True(match.Success, PropsFile + " sets no " + property + ".");

        return match.Groups["value"].Value.Trim();
    }

    /// <summary>
    /// Reads the version a manifest declares.
    /// </summary>
    /// <param name="manifest">The manifest file name at the repository root.</param>
    /// <returns>The value of its version key.</returns>
    private static string ManifestValue(string manifest)
    {
        var match = ManifestVersion.Match(RepositoryFiles.ReadFromRoot(manifest));

        Assert.True(match.Success, manifest + " declares no version.");

        return match.Groups["value"].Value;
    }

    [Fact]
    public void TheShippedVersionIsOneNumber()
    {
        var manifests = RepositoryFiles.ManifestNames();

        Assert.NotEmpty(manifests);

        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var manifest in manifests)
        {
            seen[manifest] = ManifestValue(manifest);
        }

        foreach (var property in VersionProperties)
        {
            seen[PropsFile + " " + property] = PropertyValue(property);
        }

        var distinct = seen.Values.Distinct(StringComparer.Ordinal).ToArray();

        Assert.True(
            distinct.Length == 1,
            "The release route publishes one plugin, so one number describes it. These disagree: "
                + string.Join(
                    ", ",
                    seen.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                        .Select(entry => entry.Key + " says " + entry.Value))
                + ".");
    }

    [Fact]
    public void TheShippedVersionIsFourNumericParts()
    {
        // The release route pads a three-part number to four before it compares, so a manifest
        // reading 0.1.0 and an assembly reading 0.1.0.0 pass there while being two strings here.
        // Requiring four parts everywhere is what keeps the test above and that step agreeing
        // about what a difference is.
        foreach (var manifest in RepositoryFiles.ManifestNames())
        {
            var value = ManifestValue(manifest);

            Assert.True(
                FourParts.IsMatch(value),
                manifest + " declares version " + value + ", which is not four numeric parts.");
        }

        foreach (var property in VersionProperties)
        {
            var value = PropertyValue(property);

            Assert.True(
                FourParts.IsMatch(value),
                PropsFile + " sets " + property + " to " + value + ", which is not four numeric parts.");
        }
    }
}
