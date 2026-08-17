using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.SmartCollections.Configuration;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Common.Plugins;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// <c>docs/uninstall.md</c> promises that removing this plugin leaves the collections it
/// generated alone. The server gives a plugin one moment to act on that removal:
/// <c>InstallationManager.UninstallPlugin</c> calls <c>OnUninstalling</c> on the live instance
/// before the files go. This plugin keeps the promise by overriding nothing, so what runs in
/// that moment is the server's own empty implementation.
/// </summary>
/// <remarks>
/// The promise had nothing behind it until this class. A later change adding an override that
/// deletes or renames a collection would contradict the page while every other check stayed
/// green, and uninstall is also the route somebody takes while troubleshooting, so the content
/// it destroys is content nothing restores. These tests do not need a collection writer or a
/// rule identity to exist: an override that is absent cannot delete anything, whatever the rest
/// of the plugin grows into.
/// </remarks>
public class UninstallHookTests
{
    /// <summary>
    /// The hook the server calls, named once so a rename in the server SDK is a compile-time
    /// question here rather than a test that starts passing over nothing.
    /// </summary>
    private const string HookName = "OnUninstalling";

    /// <summary>
    /// Gets the two assemblies this repository ships into a server. A type overriding the hook
    /// in either of them runs on uninstall; a type in the test assembly does not.
    /// </summary>
    private static IEnumerable<Assembly> ShippedAssemblies =>
    [
        typeof(Plugin).Assembly,
        typeof(RuleDocumentScan).Assembly
    ];

    /// <summary>
    /// The hook this plugin presents to the server is the server's own. If this assertion is
    /// ever red, read <c>docs/uninstall.md</c> before changing it: the page states what an
    /// operator was told would happen to their collections.
    /// </summary>
    [Fact]
    public void ThePluginLeavesTheUninstallHookWhereTheServerDeclaredIt()
    {
        var hook = typeof(Plugin).GetMethod(
            HookName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        // A null here is not this plugin passing. It is the server having renamed or removed the
        // hook, which turns every other assertion in this class into a statement about nothing.
        Assert.True(
            hook is not null,
            $"No parameterless {HookName} is reachable from Plugin. The server's uninstall hook "
                + "has moved, and what docs/uninstall.md promises has to be re-read against the "
                + "package rather than against this test.");

        Assert.Equal(typeof(BasePlugin), hook!.DeclaringType);
    }

    /// <summary>
    /// The assertion above reads one type. This one reads every type in both shipped assemblies,
    /// because a second plugin type, or a base class between <see cref="Plugin"/> and the
    /// server's, would carry the override past a test that only looks at <see cref="Plugin"/>.
    /// </summary>
    [Fact]
    public void NoTypeThisRepositoryShipsDeclaresAnUninstallHook()
    {
        var declared = ShippedAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => string.Equals(method.Name, HookName, StringComparison.Ordinal))
            .Select(method => $"{method.DeclaringType?.FullName}.{method.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            declared.Count == 0,
            "Something this repository ships runs on uninstall, and docs/uninstall.md says "
                + "nothing of this plugin's does: "
                + string.Join(", ", declared));
    }

    /// <summary>
    /// The configuration type is named so the generic base above is the one the plugin actually
    /// derives from rather than a coincidence of naming, and so this file fails to compile if
    /// that pairing changes.
    /// </summary>
    [Fact]
    public void ThePluginDerivesFromTheServersPluginBase()
    {
        Assert.IsAssignableFrom<BasePlugin<PluginConfiguration>>(
            (Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plugin)));
    }
}
