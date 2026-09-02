# The values this plugin runs on, and the reason for each

Three values of this kind exist today. Two intervals decide when a burst of
library changes is treated as finished, and one bound decides how many past runs
a rule's record keeps. This page is where their defaults and the reason for each
are written down, so that changing one is a decision somebody takes against a
stated reason rather than a number they found in a constructor.

| Value                | Default    | What it decides                                                                   |
| -------------------- | ---------- | --------------------------------------------------------------------------------- |
| `DefaultQuietPeriod` | `00:00:30` | How long the change stream has to be quiet before a burst is treated as finished. |
| `DefaultMaximumWait` | `00:05:00` | The longest a change waits behind a stream that keeps producing more.             |
| `DefaultDepth`       | `10`       | How many past runs a rule's refresh record keeps.                                 |

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

## Why the history keeps ten runs

It answers one question: is this rule failing, or did it fail once. The last few
runs answer that, and every run of every rule kept for the life of a server
nobody restarts is growth with no ceiling.

Ten is a week of daily refreshes with room for the manual ones an operator makes
while repairing a rule. **That number is not measured against anything.** No
refresh has ever run on a server, so unlike the two intervals above it is a
defensible starting bound rather than a figure taken from a behaviour, and this
sentence stays here until one is.

One thing the record cannot do, which matters when reading it. It holds no
instant, because the instant a run used is the instant its rule resolved relative
dates against - an input to an evaluation, recorded with its result, which is
what the invariant lint's `ambient-clock-in-the-engine` refuses the alternative
to. Nothing evaluates a rule yet, so the record can say that the last four runs
failed and cannot say over how many days.

## None of the three is settable, and the configuration declares nothing

All three are constructor arguments with the defaults above, handed in at
registration or at construction. Changing any of them is a change to this
repository and a new build, not something an operator can do on a running
server.

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

## Settings that switch a check off do not exist

Three of them by name: nothing disables the validation a rule document goes
through, nothing raises the nesting limit, and nothing turns off the ownership
check that stops this plugin writing to a collection it did not create. A server
whose administrator can switch a safety property off has that property only
until somebody is in a hurry.

`PluginConfigurationTests.NoSettingSwitchesOffAValidationOrAnOwnershipCheck`
reads the names the configuration declares and refuses one that pairs a
switching-off word with validation or ownership, or that names the nesting limit
at whatever verb. It is vacuous today, because the configuration declares
nothing; it is here before the first setting rather than after it, and it is
held to a table of names it has to refuse and near misses it has to pass, so
what it does is proved without a setting existing.

WHAT IT READS IS A NAME. A property called `StrictMode` whose `false` value
switches validation off passes it, and so does anything else that reaches the
switch through a value rather than through a spelling. What it refuses is the
name somebody reaches for when they want the switch, which is the one that
arrives without an argument being had about it.

## What holds this page

`SettingsDocumentTests` reads the table above and the public static fields the
declaring types carry, and compares them in both directions. A value whose
default moves without its row moving reds the suite, and so does a row naming a
value no type declares.

The comparison is derived rather than listed. The test asks each type for its
fields instead of carrying a list of names, so a fourth value added tomorrow is
covered by this page's obligation on the day the field appears, without anybody
remembering to extend a test.

WHAT IT DOES CARRY IS A LIST OF TYPES, and until now that was a hole rather than
a bound: a value declared on a type outside the list reached no row here and
nothing said so, so both directions of the comparison stayed green over a value
nobody had written down. The list is still held by hand and it is no longer
silent. `EveryDefaultTheShippedAssembliesDeclareIsOnATypeThisTestReads` walks
the two shipped assemblies for a public static field whose name begins with
`Default` and reds on one the list does not reach, naming the type and the
field.

THE BOUND THAT IS LEFT IS THE NAME. That search finds a default by what it is
called, so a value of this kind called something else is seen by neither the
search nor the comparison. The trade is worth stating rather than leaving to be
discovered: a type is invisible in the diff that adds a field to it, and a name
is in that diff, so the reader of a change is now being asked about something in
front of them instead of about nothing. The refusal also stops short of deciding
anything - the same assemblies declare the nesting limit, the identifier length
and the schema versions, which are rule-language limits with their own pages -
so a default outside the list reds and whoever added it says which of the two it
is.

A field of a kind the test cannot write out reds it with a message naming the
field, rather than being skipped: skipping would take a value out of this page's
obligation silently, which is the failure the comparison exists against.

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
