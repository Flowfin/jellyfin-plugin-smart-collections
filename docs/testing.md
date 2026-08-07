# How the tests run

Every test in this repository runs on a machine with no display attached, under
an unprivileged account, and without writing to any machine-wide trust store,
service registry or system path.

A test that needs any of those runs for one person on one machine. It passes
there, it reports nothing to anyone else, and the day it starts failing nobody
finds out. That is the reason for the rule, and it is worth more than any
individual test the rule refuses.

The rule is about the suite as a whole, so the tests it refuses are named here
along with what replaces each one. A refusal without a replacement is a hole,
not a rule.

## A browser-driven test of the configuration page

Refused. It needs a browser runtime and a rendering surface, and it is the
slowest and least reliable way to learn that a form posted a field.

What replaces it, in three parts.

The API behind the page is tested directly, including its authorisation, so the
page is a thin client over something that can be exercised without opening it.
The API is #47 and the refresh endpoint it shares a surface with is #36.

A test asserts that the field and operator lists the page shows come from the
server's vocabulary endpoint rather than from a copy in JavaScript, so the page
cannot drift from the language it is offering. That is #49.

A test parses the page as text and asserts that every configuration property has
a control and every control has a property. Both directions exist now, in
`Jellyfin.Plugin.SmartCollections.Tests/PluginConfigurationTests.cs`, as
`EveryControlOnTheSettingsPageBindsToASetting` and
`EverySettingHasAControlOnTheSettingsPage`. Both are vacuous while the plugin
declares no setting, which is the state of the plugin rather than a gap in the
pair.

## A test that installs the plugin or registers a service

Refused, because it needs elevation.

What replaces it: every path the plugin uses comes from the server's
`IApplicationPaths`, and the tests point that at a temporary directory they
create and remove themselves. The store already works this way, taking its
directory as a constructor argument rather than looking one up, and
`RuleDocumentStoreTests` creates and deletes its own directory under the
machine's temporary path. Which directory the running plugin passes is decided
where the plugin's services are registered, which is #70.

## A test that installs a certificate into a machine trust store

Refused. It needs elevation and it changes the machine outside the test.

Nothing replaces it, because nothing needs it. The rule engine makes no outbound
network call at all. The guard that keeps this true rather than merely current
is the invariant lint refusing an HTTP client type inside the engine assembly,
which is #16, over the assembly boundary #68 created. That boundary is the
`Jellyfin.Plugin.SmartCollections.Engine` project, and the lint names its path
rather than describing it, so a file added anywhere in the engine is in scope
without the rule being edited.

## A test that boots a Jellyfin server to prove a unit-level property

Refused. It is neither a display nor elevation, but it makes a determinism test
depend on a database and a startup sequence.

What replaces it: the library query and collection write surfaces sit behind
narrow interfaces with fakes, which is what #30 and #33 build on and what #68
makes expressible. The one place a running server is the right answer is the
interoperability matrix, which boots containers on an ordinary runner, needs no
display and no elevated rights, and proves a property no fake could. That is
#59.

## What holds the rule

`SuitePortabilityTests.NoTestInTheSuiteReferencesAnAbsoluteSystemPath` reads
every C# source in the test project and refuses three shapes: a drive-rooted
path, a path under a system root, and a Windows environment folder. Build output
is excluded, because the generated files under `obj` hold paths from the machine
that built them and nobody writes those.

A second leg, `TheScanReadsEveryTestSourceInTheProject`, exists because a scan
that reads nothing passes the first one silently.

Run it with the rest of the suite:

```
dotnet test -c Release --filter FullyQualifiedName~SuitePortabilityTests
```

## What this does not cover

The scan reads the test project only. The plugin project takes its directories
as arguments rather than naming them, which is the design the second refusal
above describes, and no check in this tree refuses an absolute path appearing
there. Where that belongs is #70.

The share notation Windows uses for a network host is not among the three
shapes. Written as a pattern it is two backslashes, which is also what an
ordinary escaped backslash in a C# string looks like, so it would match a great
deal of innocent code and its own source besides. That shape is not refused and
this sentence is the whole of what is claimed about it.

The rest of the rule is prose. Nothing in this repository refuses a test that
opens a browser, asks for elevation or writes to a trust store. The absolute
path scan is the one part with a machine behind it, and it is a small part of
what the rule says.
