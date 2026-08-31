# The values this plugin runs on, and the reason for each

Two intervals decide when a burst of library changes is treated as finished.
They are the only values of this kind the plugin has today, and this page is
where their defaults and the reason for each are written down, so that changing
one is a decision somebody takes against a stated reason rather than a number
they found in a constructor.

| Value                | Default    | What it decides                                                                   |
| -------------------- | ---------- | --------------------------------------------------------------------------------- |
| `DefaultQuietPeriod` | `00:00:30` | How long the change stream has to be quiet before a burst is treated as finished. |
| `DefaultMaximumWait` | `00:05:00` | The longest a change waits behind a stream that keeps producing more.             |

## Why the quiet period is thirty seconds

It is bounded from both ends and the two bounds are close together.

Short enough that one film added by hand reaches its collection while the person
who added it is still looking at the screen. A quiet period of several minutes
turns a one-item edit into something an operator reports as broken before it
finishes.

Long enough that the gaps a library scan leaves between batches of items do not
each close a burst of their own. A scan that pauses for a few seconds between
batches would, under a two-second quiet period, produce one evaluation per
batch, which is the fan-out the coalescer exists to prevent.

## Why the maximum wait is five minutes

It bounds how stale a collection can be during an import that runs for hours. A
burst that keeps being extended by the next change would otherwise never close,
and a library import is exactly the shape that extends it.

It is also far enough above the quiet period that an ordinary burst never
reaches it. That gap is not decoration: a batch carries the reason it closed,
and a maximum set close to the quiet period would make every batch report the
maximum, which turns an informative field into a constant.

## Neither is settable, and the configuration declares nothing

Both are constructor arguments with the defaults above, handed in at
registration. Changing either one is a change to this repository and a new
build, not something an operator can do on a running server.

```
git show origin/master:Jellyfin.Plugin.SmartCollections/Configuration/PluginConfiguration.cs \
  | grep -cE '^\s*public .*\{ get;'
0
git show origin/master:Jellyfin.Plugin.SmartCollections/Configuration/configPage.html \
  | grep -cE '<input|<select|<button|<table'
0
```

The configuration class declares no property and the settings page carries no
control, so there is nothing on the page for either value to appear as. A
setting arrives with the surface that reads it, and the page that would carry
one is #47.

## What holds this page

`SettingsDocumentTests` reads the table above and the public static `TimeSpan`
fields the coalescer declares, and compares them in both directions. A value
whose default moves without its row moving reds the suite, and so does a row
naming a value the type does not declare.

The comparison is derived rather than listed. The test asks the type for its
fields instead of carrying a list of names, so a third interval added tomorrow
is covered by this page's obligation on the day the field appears, without
anybody remembering to extend a test.

## What is not on this page

The plan names three more values of this kind: how often the scheduled refresh
runs by default, the page size an evaluation reads a library in, and whether
library events trigger an evaluation at all. None of the three exists in the
tree.

```
git grep -ln 'IScheduledTask' origin/master -- '*.cs' ; echo "exit=$?"
exit=1
git grep -lni 'pagesize\|page size' origin/master -- '*.cs' ; echo "exit=$?"
exit=1
```

The first arrives with the scheduled task, which is #34. The second arrives with
the bound on what one evaluation reads, which is #37. It used to name #30 beside
it; #30 is closed and what it landed compiles a condition onto a query without
reading a library, so a reader following that pointer met a finished issue and no
page size. The third is a switch over a subscription
that today is always registered, and it is worth having only once there is
something behind it to switch off. Writing a default and a reason for any of the
three now would be writing about a value nothing reads, which is the shape this
page exists to replace rather than to imitate.
