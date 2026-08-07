using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The plugin's identifier is written in three places and read by three different things: the
/// catalogue reads <c>build.yaml</c>, the server reads <see cref="Plugin.Id"/>, and the
/// configuration page passes its own copy back to the server when settings are saved. The three
/// live in three file formats, nothing compares them, and a disagreement is silent until an
/// install collides or a save goes to the wrong plugin.
/// </summary>
public class PluginIdentityTests
{
    /// <summary>
    /// The identifier every copy of the Jellyfin plugin template ships with. Two plugins
    /// presenting it to one server is an install collision.
    /// </summary>
    private const string TemplateGuid = "eb5d7894-8eef-4b36-aa6f-5d124e828ce1";

    // build.yaml is a flat manifest and the key is a scalar at column zero. Matching it directly
    // keeps the test project free of a YAML dependency it would otherwise carry for one value.
    // The match is asserted rather than assumed, so a manifest that stops declaring a guid in
    // this shape fails here instead of passing quietly.
    //
    // The optional carriage return is not decoration. A checkout on Windows gives this file CRLF
    // line endings and one on the runner gives it LF, and a pattern ending [ \t]*$ passes on the
    // runner and fails on a developer's machine for a reason that has nothing to do with the id.
    private static readonly Regex ManifestGuid = new(
        "^guid:[ \t]*\"?(?<guid>[0-9a-fA-F-]{36})\"?[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    [Fact]
    public void ManifestGuidAndPluginIdAreTheSameValue()
    {
        var manifest = RepositoryFiles.ReadFromRoot("build.yaml");
        var match = ManifestGuid.Match(manifest);

        Assert.True(match.Success, "build.yaml declares no guid: scalar this test can read.");

        var declared = Guid.Parse(match.Groups["guid"].Value);

        Assert.Equal(declared, PluginId());
    }

    // The quote around the identifier is the formatter's to choose, not this test's. Prettier
    // reads the page's inline script and normalises string delimiters, so an assertion naming one
    // quote character fails the day the formatter prefers the other, for a reason that has nothing
    // to do with which plugin the page saves to. Either delimiter is matched and the pair has to
    // agree, so a half-quoted value is still refused.
    private static readonly Regex PageUniqueId = new(
        "pluginUniqueId:[ \t]*(?<quote>['\"])(?<guid>[0-9a-fA-F-]{36})\\k<quote>",
        RegexOptions.CultureInvariant);

    [Fact]
    public void ConfigurationPageSendsSettingsToThisPluginsId()
    {
        var page = RepositoryFiles.ReadFromRoot(
            "Jellyfin.Plugin.SmartCollections/Configuration/configPage.html");

        var match = PageUniqueId.Match(page);

        Assert.True(match.Success, "configPage.html passes no pluginUniqueId this test can read.");

        Assert.Equal(PluginId(), Guid.Parse(match.Groups["guid"].Value));
    }

    [Fact]
    public void NothingStillCarriesTheTemplateGuid()
    {
        Assert.NotEqual(Guid.Parse(TemplateGuid), PluginId());
        Assert.DoesNotContain(TemplateGuid, RepositoryFiles.ReadFromRoot("build.yaml"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            TemplateGuid,
            RepositoryFiles.ReadFromRoot("Jellyfin.Plugin.SmartCollections/Configuration/configPage.html"),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads <see cref="Plugin.Id"/>, the value the server keys on.
    /// </summary>
    /// <remarks>
    /// The override is a constant expression that reads no state, and constructing a
    /// <see cref="Plugin"/> for real would mean standing up the server interfaces its base
    /// constructor takes. An uninitialised instance is enough to read it and keeps the test
    /// reading the shipping property rather than a copy of its literal.
    /// </remarks>
    /// <returns>The identifier the plugin advertises to the server.</returns>
    private static Guid PluginId()
        => ((Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin))).Id;
}
