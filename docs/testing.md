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
in one place, `PluginServiceRegistrator.RulesDirectory`, which composes it from
the paths the server hands out and never from the working directory or an
environment variable. That is what lets a test point the store at a directory it
owns rather than at the one a server would use.

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

What replaces it, for the half of it that exists. The surfaces a refresh touches
sit behind interfaces narrower than the server's own, and one of the two is
built. The write surface is `ICollectionMembershipWriter`, three calls over
identifiers rather than the server's collection manager, and the suite drives it
through `FakeCollectionWriter` in `MembershipApplierTests`.

A query surface is built for one question, and it is not the one an evaluation
asks. `CollectionStamp.LookupQuery` composes an `InternalItemsQuery` that finds
the collection a rule owns by the mark this plugin wrote on it, the port over it
is `ICollectionOwnership`, and the suite drives that port through
`FakeCollectionOwnership` in `CollectionResolverTests`, which matches on a
provider key and its value together the way the server's own query does.

Neither port has a server side in this tree. `ICollectionMembershipWriter` has
stood without one since it was declared and `ICollectionOwnership` arrives the
same way, because each adapter is a forward onto `ILibraryManager` or
`ICollectionManager` and no test here can execute one: an `ILibraryManager` is
eighty-four members, and holding a real one means the running server this page
refuses. Both therefore arrive with the first trigger that runs a refresh, which
needs both at once. What the suite holds meanwhile is the decision in front of
each port, `CollectionResolver` and `MembershipApplier`, neither of which names
a server type.

A rule's query is composed AND ANSWERED now, which is the third state this
paragraph has recorded and is the one the sentence below is about.
`RuleQueryTable` declares the
pairs of a field and an operator the server's query carries, `RuleQueryRow`
holds each pair's write, `RuleQueryCompiler` walks a rule's conditions onto one
query and `RuleQueryCompilation` carries what came out. Every one of those is
asserted without a server, because a query is an object with properties and the
suite reads them by reflecting over the type the build resolved.

THE PORT AND ITS FAKE ARE HERE, AND THIS PARAGRAPH SAID THEY WERE NOT. What
stood here recorded that nothing asked the server for the items a compiled query
selects, that there was no interface over `ILibraryManager.GetItemList` and no
fake behind one, and that running a rule against a library was something this
suite could not express. `IRuleItemSource` is that interface, one method over a
query and a list of items; `LibraryManagerItemSource` is the adapter that hands
the query to the server; and the suite drives the port through
`FakeRuleItemSource` in `RuleEvaluatorTests`, which records every query it was
asked and can answer in the reverse of the order it was filled.

`FakeLibraryChangeSource` is unchanged and is still not that fake. It raises the
three library events and holds no items, so it stands in for what starts an
evaluation rather than for what an evaluation reads, and the two are named apart
here because a reader looking for one of them will meet the other first.

The adapter has no test and cannot have one, for the reason
`LibraryManagerChangeSource` has none: the only way to execute it is to hold a
real `ILibraryManager`, which means the running server this page refuses. The
residual is one forwarding line a reader checks by eye, and it is the second of
two in this tree rather than a new kind of gap.

The one place a running server is the right answer is the interoperability
matrix, which boots containers on an ordinary runner, needs no display and no
elevated rights, and proves a property no fake could. The section below is the
part of that matrix which is in the tree.

## The one place a server is booted

The alone case of that matrix is in the tree. It boots a server per supported
line with the packaged zip installed and nothing else, and asserts three things:
the startup log holds no error from this plugin, the plugin is listed as loaded
and active, and this plugin's surface answers an administrator and refuses an
anonymous caller.

It is one command, and the same command is what the runner invokes:

```
node .github/scripts/boot-a-line-with-only-this-plugin.js \
  --image jellyfin/jellyfin:10.11.11 --package <package.zip>
```

The package is the one the shipping check builds rather than the build output,
because a harness that loads `bin/` proves the code works and not that the thing
users install works.

It stays inside the rule at the top of this file. The server answers plain HTTP
on a loopback port, so nothing trusts a certificate, and nothing is installed
onto the machine. What it does need is a container runtime, and it starts none:
where no daemon answers, it says so and stops rather than reaching for one.

What that harness does not reach is written in its own header, and the shortest
version is that this plugin declares no endpoint of its own yet, so the third
assertion runs against the server's plugin surface keyed on this plugin's
identifier. An endpoint this plugin ships is #47.

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

## Regenerating the rule corpus

`Jellyfin.Plugin.SmartCollections.Tests/rules/` holds one rule document per
`.json` file and, beside each one, a `.expected.txt` holding what it compiles
to. The suite compares the two on every run. That directory's own README says
what an expected file holds and what it deliberately does not.

Where a change to the engine moves an answer on purpose, rewrite the files
rather than editing them by hand:

```
SMART_COLLECTIONS_REWRITE_EXPECTED=1 dotnet test -c Release --filter FullyQualifiedName~RuleCorpusTests
```

Then read the diff, and run the suite again without the variable.

**A regeneration is always red**, by one test that says the files were rewritten
rather than compared. That is what stops a route with the variable set from
reporting a green run over answers it had just replaced, and it is why the
command above is a thing somebody types rather than a step in a workflow. No
workflow in this repository sets that variable:

```
git grep -c SMART_COLLECTIONS_REWRITE_EXPECTED -- .github/
exit=1
```

What the rewrite keeps is the leading run of `#` lines in each expected file,
which is the sentence saying why that document is in the corpus. What it
replaces is everything below them.

## What the suite would catch if it broke

Nothing above measures that. Whether a change to the engine would be noticed by
a test is measured separately, by a mutation run that seeds one fault at a time
and reports which ones nothing failed on. What the gate holds is the surviving
set rather than a score, and the record, the floor and the commands that read
both are in [mutation-score.md](mutation-score.md).
It runs on a schedule rather than on a pull request, and that document says what
that costs.

## What this does not cover

The scan reads the test project only. The plugin project takes its directories
as arguments rather than naming them, which is the design the second refusal
above describes, and no check in this tree refuses an absolute path appearing
there. What holds it is that design and whoever reads a change, and this
sentence is the whole of what is claimed about it.

The share notation Windows uses for a network host is not among the three
shapes. Written as a pattern it is two backslashes, which is also what an
ordinary escaped backslash in a C# string looks like, so it would match a great
deal of innocent code and its own source besides. That shape is not refused and
this sentence is the whole of what is claimed about it.

The rest of the rule is prose. Nothing in this repository refuses a test that
opens a browser, asks for elevation or writes to a trust store. The absolute
path scan is the one part with a machine behind it, and it is a small part of
what the rule says.
