using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SmartCollections.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
/// <remarks>
/// This declares nothing, because the plugin reads nothing. A setting is written by an
/// administrator and read back by code that behaves differently for it, and there is no such
/// code yet: no rule loader, no scheduled refresh, no collection writer. A property that
/// arrives before the code reading it is a control on the settings page that does nothing,
/// which is what this class held when it came from the plugin template.
///
/// Settings arrive with the surface that reads them. Which ones there will be, what each
/// defaults to and the reason for each are planned on the tracker rather than guessed at here.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
}
