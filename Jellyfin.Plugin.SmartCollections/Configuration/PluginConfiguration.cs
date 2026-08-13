using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SmartCollections.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
/// <remarks>
/// This declares nothing, because nothing in the plugin behaves differently for a setting. A
/// setting is written by an administrator and read back by code that changes what it does for
/// it, and every value that could be one is fixed where it is used: the rules directory is
/// composed from the paths the server hands out, and the two intervals the coalescer runs on
/// are constants with the reason for each written beside them. What would read a setting next
/// is a scheduled refresh and a collection writer, and neither exists. A property that arrives
/// before the code reading it is a control on the settings page that does nothing, which is
/// what this class held when it came from the plugin template.
///
/// Settings arrive with the surface that reads them. Which ones there will be, what each
/// defaults to and the reason for each are planned on the tracker rather than guessed at here.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
}
