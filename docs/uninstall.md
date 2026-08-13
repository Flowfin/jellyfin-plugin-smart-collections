# What uninstalling does to the collections

Uninstalling this plugin leaves every collection it generated in the library,
exactly as the last refresh left it. Nothing is deleted, nothing is renamed and
nothing is emptied. The collections become ordinary Jellyfin collections that
nobody maintains any more, with their ownership stamp still on them, and
reinstalling the plugin adopts them again by that stamp rather than building a
second set beside them.

Read this before you install, not afterwards. A plugin that can create visible
library content should say what it does with that content when it is removed,
and the answer here is that it does nothing.

## The moment the server offers, and what this plugin does with it

The server does give a plugin one moment on the way out. `OnUninstalling` is
declared on `IPlugin`, implemented as a virtual no-op on `BasePlugin`, and
called from the installation manager. The default body is empty on both
supported lines, at the same line number:

```
for r in v10.11.9 master; do
  gh api "repos/jellyfin/jellyfin/contents/MediaBrowser.Common/Plugins/BasePlugin.cs?ref=$r" \
    --jq .content | tr -d '\n' | base64 -d | grep -n -A2 'virtual void OnUninstalling'
done
76:        public virtual void OnUninstalling()
77-        {
78-        }
76:        public virtual void OnUninstalling()
77-        {
78-        }
```

This repository overrides nothing there, so the empty body above is what runs:

```
git grep -n 'OnUninstalling' -- '*.cs' ; echo "exit=$?"
exit=1
```

The behaviour is therefore delivered by not writing anything into that hook,
which is the cheapest way to hold it and the easiest to lose. What stops a later
change from writing a deletion into it is a test, and that test does not exist
yet. See the last section, which says so rather than implying otherwise.

## Why the collections stay

Somebody removing a plugin to try something is on exactly the route that reaches
the hook, and that is the moment a plugin deleting library content would destroy
work nothing restores. A household's collections are not the plugin's to spend
on a troubleshooting step.

Leaving them costs an operator a tidy-up if they wanted them gone. Deleting them
costs an operator everything the rules produced, with no way back short of
writing the rules again and waiting for a refresh. The two costs are not
comparable, so the choice is not a close one.

One reason that would look like it belongs here does not. An update is a
different route and does not reach the hook at all, so nothing about upgrade
paths argues for this behaviour:

```
gh api "repos/jellyfin/jellyfin/contents/Emby.Server.Implementations/Updates/InstallationManager.cs?ref=master" \
  --jq .content | tr -d '\n' | base64 -d \
  | grep -n 'isUpdate = await\|PluginUpdatedEventArgs\|public void UninstallPlugin\|OnUninstalling()'
324:                var isUpdate = await InstallPackageInternal(package, linkedToken).ConfigureAwait(false);
335:                    await _eventManager.PublishAsync(new PluginUpdatedEventArgs(package)).ConfigureAwait(false);
385:        public void UninstallPlugin(LocalPlugin plugin)
398:            plugin.Instance?.OnUninstalling();
```

Those are the two routes in that file, and the hook sits in the second. What is
measured there is that the update route does not call `UninstallPlugin` and that
`UninstallPlugin` holds the only call to the hook. Whether anything else on an
upgrade disturbs a collection is a different question and is not evaluated here.

## Two bounds on the promise

The hook runs only where the plugin has a live instance, and only where the
plugin says it may be uninstalled at all:

```
gh api "repos/jellyfin/jellyfin/contents/Emby.Server.Implementations/Updates/InstallationManager.cs?ref=v10.11.9" \
  --jq .content | tr -d '\n' | base64 -d | sed -n '382,395p'
        public void UninstallPlugin(LocalPlugin plugin)
        {
            if (plugin is null)
            {
                return;
            }

            if (plugin.Instance?.CanUninstall == false)
            {
                _logger.LogWarning("Attempt to delete non removable plugin {PluginName}, ignoring request", plugin.Name);
                return;
            }

            plugin.Instance?.OnUninstalling();
```

So a promise about what happens on uninstall is a promise about a server where
this plugin was loaded. On a server where it failed to load there is no instance
and the call is skipped, and the collections stay for the plainer reason that no
code of this plugin's ran.

The second bound is the same one from the other end. Removing the plugin's files
from the server's plugin directory by hand runs none of this plugin's code
either, so it leaves the collections where they are as well. That is a claim
about a route that executes nothing rather than a measurement of one.

## If you wanted the collections gone

Delete them in Jellyfin the way you would delete any collection. The plugin's
own action for removing what it generated is planned on the tracker in #57 and
does not exist yet, so today the tidy-up is a manual one.

## What holds this, and what does not

Nothing in the suite refuses a change that would delete a collection on
uninstall. The guard this document is owed is a test asserting that the hook
deletes no collection and removes no stamp, and a second asserting that a
reinstall adopts the stamped collections rather than creating duplicates.
Neither can be written yet, because a rule has no identity to stamp with and
nothing in either shipped assembly writes a collection into the server:

```
node -e "const s=require('./Jellyfin.Plugin.SmartCollections.Engine/Rules/rule-document.schema.json');console.log(Object.keys(s.properties).join(', '))"
schemaVersion
git grep -n 'CreateCollection' -- '*.cs' ; echo "exit=$?"
exit=1
```

The identity is #29 and the two tests are the open half of #56. Until they land,
what stands behind the first paragraph of this page is the absence measured
above and a reader who checks it, not a check that runs.
