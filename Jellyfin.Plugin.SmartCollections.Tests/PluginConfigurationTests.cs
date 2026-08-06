using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Configuration;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A plugin setting is only real where three things agree: the property exists, a fresh
/// configuration gives it a stated default, and the settings page offers a control for it. The
/// template shipped four settings where none of that mattered because nothing read them. These
/// tests hold the three together so the next setting cannot arrive half-made.
/// </summary>
public class PluginConfigurationTests
{
    /// <summary>
    /// Every setting the configuration declares, with the value a fresh instance is documented
    /// to give it. This table is the documentation: a property missing from it is a setting
    /// whose default nobody wrote down, and an entry with no property is a setting that was
    /// removed without its row.
    /// </summary>
    /// <remarks>
    /// Empty, and that is the current state of the plugin rather than an omission. Nothing in
    /// the plugin reads a setting yet.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, object?> DocumentedDefaults =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// The form controls a settings page can carry. A control is bound to a setting by its id,
    /// which is what the page's own script passes to <c>querySelector</c>, so the id is what is
    /// compared. Buttons are not matched: a submit button carries no value.
    /// </summary>
    private static readonly Regex FormControl = new(
        "<(?:input|select|textarea)\\b[^>]*?\\bid=\"(?<id>[^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the settings the configuration class actually declares. Declared only, so the
    /// members every plugin configuration inherits are not mistaken for this plugin's settings.
    /// </summary>
    /// <returns>The declared public instance properties.</returns>
    private static PropertyInfo[] DeclaredSettings()
        => typeof(PluginConfiguration).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    [Fact]
    public void EverySettingHasADocumentedDefault()
    {
        Assert.Equal(
            DocumentedDefaults.Keys.OrderBy(name => name, StringComparer.Ordinal),
            DeclaredSettings().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void AFreshConfigurationHoldsTheDocumentedDefaults()
    {
        var configuration = new PluginConfiguration();

        foreach (var (name, expected) in DocumentedDefaults)
        {
            var property = typeof(PluginConfiguration).GetProperty(name);

            Assert.True(property is not null, "No setting is named " + name + ".");
            Assert.Equal(expected, property!.GetValue(configuration));
        }
    }

    [Fact]
    public void NoneOfTheTemplatesSampleSettingsSurvive()
    {
        var declared = DeclaredSettings().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var sample in new[] { "SomeOptions", "TrueFalseSetting", "AnInteger", "AString", "Options" })
        {
            Assert.DoesNotContain(sample, declared);
        }
    }

    [Fact]
    public void EveryControlOnTheSettingsPageBindsToASetting()
    {
        var page = RepositoryFiles.ReadFromRoot(
            "Jellyfin.Plugin.SmartCollections/Configuration/configPage.html");

        foreach (Match control in FormControl.Matches(page))
        {
            var id = control.Groups["id"].Value;

            Assert.True(
                DocumentedDefaults.ContainsKey(id),
                "The settings page carries a control with id " + id + ", and no setting is named that.");
        }
    }
}
