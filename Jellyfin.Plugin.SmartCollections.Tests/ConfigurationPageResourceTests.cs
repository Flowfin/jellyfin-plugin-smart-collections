using System.Globalization;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The configuration page is shipped as an embedded resource and located at run time by a
/// path built from the plugin's namespace. Nothing in the compiler checks that the built
/// path names a resource that exists, so the page goes missing silently on a rename.
/// </summary>
public class ConfigurationPageResourceTests
{
    [Fact]
    public void ConfigurationPageIsEmbeddedUnderThePathThePluginAdvertises()
    {
        var pluginType = typeof(Plugin);

        // The same expression GetPages() uses to build EmbeddedResourcePath.
        var advertised = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.Configuration.configPage.html",
            pluginType.Namespace);

        Assert.Contains(advertised, pluginType.Assembly.GetManifestResourceNames());
    }
}
