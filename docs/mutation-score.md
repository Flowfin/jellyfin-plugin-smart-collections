# The mutation gate, and what it holds

The coverage floor answers whether a line was executed. It cannot answer whether
a change to that line would be noticed, and those are different questions: a
test that runs a line and asserts nothing about it holds the floor and catches
nothing.

Mutation testing answers the second question. The tool seeds one fault at a time
into the engine assembly, runs the tests that cover the mutated line, and reports
which faults nothing failed on. A surviving mutant is a statement about the
suite: either a test that was never written, or a piece of code nobody needs.

## Running it

```
dotnet tool restore
dotnet stryker --concurrency 1
```

Everything else the run needs is in `stryker-config.json` and the tool version is
pinned in `.config/dotnet-tools.json`, so a clone reproduces the record rather
than a nearby one. Reports land under `StrykerOutput/`, which is untracked.

The concurrency is part of the measurement rather than a speed setting, and the
section below is why. `.github/workflows/mutation.yml` passes the same number,
and `.github/scripts/mutation-record.js` refuses a workflow and a record that
disagree about it.

## What the gate holds

`mutation-record.json`, and no percentage anywhere. It names three things:

- **`survivors`** - every mutant nothing in the suite noticed, by file, line,
  column, mutator and replacement. This is the list to work from: writing a test
  that kills one of these is the point, and deleting the code a mutant sits in is
  also a legitimate answer.
- **`unstable`** - every mutant whose verdict moved between the runs the record
  was taken from, with the verdicts seen and the runs that saw each. These are
  excluded from the comparison and from the floor, because a mutant that is
  killed on one run and survives on the next is evidence about the run and not
  about the suite.
- **`killed`** - the killed count over what is left, which is the floor.

Each row is a tuple rather than an object, in the order the record's own
`columns` names, so a new survivor is one added line in a diff rather than a
block a reviewer has to assemble. The check refuses a record whose `columns` are
not the order it reads.

No figure is written into this page, and that is deliberate: a number restated in
a document drifts against the thing it describes, and this page carried four such
figures until the gate changed. Derive them instead:

```
node -e "const r=require('./mutation-record.json'); console.log('survivors', r.survivors.length, '| unstable', r.unstable.length, '| killed floor', r.killed, '| concurrency', r.concurrency)"
```

And to judge a run you have just taken against it:

```
node .github/scripts/mutation-record.js StrykerOutput
```

When a change moves the set on purpose, the same script writes the record the run
implies, and the formatter settles its layout the way it settles every other file
here. TAKE THAT RUN FROM THE RUNNER RATHER THAN FROM YOUR CLONE - the section
below is why, and it is two measured reasons rather than a preference:

```
gh workflow run mutation.yml --repo Flowfin/jellyfin-plugin-smart-collections --ref <your branch>
gh run download <the run> --name mutation-report --dir report
node .github/scripts/mutation-record.js report mutation-record.json --print-record > mutation-record.json.new
mv mutation-record.json.new mutation-record.json
npx prettier --write mutation-record.json
```

A run in a clone is still worth taking, and is what says whether the set is
stable at all before a record is written from anything; it is the record's
CONTENTS that belong to the machine the gate runs on.

It carries the `unstable` list forward rather than deriving it, because one run
cannot see instability: that takes two verdicts on one mutant and the runs that
saw each, which is what the entry has to name.

WHAT THAT COSTS IS WORTH SAYING PLAINLY. Nothing refuses a figure typed into this
page tomorrow. The check that re-extracted four of them is gone because there is
nothing left here for it to re-extract, not because the class stopped mattering,
and the review is what stands in its place.

## Why a score cannot be the gate

Because it is a fact about a run rather than about the tree. Three consecutive
runs on an unchanged tree at the tool's default concurrency, nothing edited
between them, measured on 2026-09-02 and recorded on #200:

```
[..] The final mutation score is 91.31 %
[..] The final mutation score is 91.44 %
[..] The final mutation score is 91.31 %
```

The two that agree to the hundredth disagree about WHICH mutants survived: one
missed a mutant the other killed, and it was a different mutant each time. So two
runs agreeing on a number is not evidence that the run reproduced, and a check
comparing scores cannot see a set that moved underneath one.

A score is also a sum, so it moves whenever any one verdict does, and a threshold
under it reds an unchanged tree while a threshold over it lets the suite weaken
by however wide the gap was. Both directions were live here: the scheduled run of
2026-08-31 crashed the tool against its own break with nothing changed, and
before that the threshold sat three points under the score for weeks. Issue #200
carries the measurements and the arrangement decided on them.

A set moves only where a verdict does, and the check can then say which mutant
and in which direction. That is what `mutation-record.json` is.

## Why the run is at concurrency one

Because that is the one variable that made the survivor set move. Measured over
nine runs in three arrangements, three runs each: with the test classes running
in parallel six mutants moved between survived and killed; with them in sequence
none did, and the only movement left was the killed-and-timed-out split below.

Four of the six sit in the static `Table` initializer of a rule table, which is
the case the tool's own documentation names under `coverage-analysis`: a mutant
inside a static initializer is covered by whichever test touched the type first,
and with classes in parallel that is a different test on every run. The others
stop moving under the same arrangement and the reason for them is not
established.

What it costs is wall clock, and the workflow's timeout is sized for it rather
than for the parallel run it replaced.

## What the split between killed and timed out does not mean

A mutant that hangs the tests is counted as killed, because a test suite that
never finishes is a test suite that noticed. Which of the two a given mutant
lands in moves between runs on the same tree, since it depends on how long the
test host took that time. This is the movement that leaves the sum alone, it is
present under every arrangement measured, and it is why `additional-timeout` in
`stryker-config.json` is 30000: without it a slow test host start is read as a
hung mutant, which does not move the count but does move a dozen mutants into a
column where they look like something worth investigating.

Two runs on the tree of 2026-08-17, minutes apart, which is the pair that showed
it and is kept as the reading it was rather than restated of a tree it was not
taken on:

```
Killed: 62, Timeout: 0  ->  62/75  ->  82.67 %
Killed: 61, Timeout: 1  ->  62/75  ->  82.67 %
```

## Why the record is taken from a run on the runner, and not from a clone

Because it is a fact about the machine the gate runs on, and two things measured
here say so. Both were found by dispatching this workflow on the branch that
built the record and reading what it refused; a record written from a Windows
clone and judged on `ubuntu-latest` failed on each of them in a different way.

**A replacement carries the checkout's line endings.** A mutant's identity here
includes the text the tool would substitute, and a multi-line replacement written
on a CRLF checkout holds a carriage return that the same mutant on the runner
does not. Two entries dangled and the identical two mutants arrived as unnamed
survivors. `.github/scripts/mutation-record.js` drops the carriage return from
every replacement it reads out of a report and refuses a record that carries one,
so this cannot come back in silence.

**The suite's own behaviour depends on the machine.** Three mutants in
`ItemFieldReader.Instant` are killed on a machine whose local time zone is not
UTC and survive on one where it is:

```
private static DateTimeOffset Instant(DateTime value)
    => value.Kind == DateTimeKind.Unspecified
        ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
        : new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero);
```

Where the local zone IS UTC the two arms compute the same instant, so nothing in
the suite separates them and all three mutants survive. They are in the record as
survivors because that is what the machine the gate runs on sees, and they are
three tests worth writing rather than a quirk of the report.

That is what a set-shaped record buys that a score never could: the score on the
runner and the score here differ by a tenth of a point, and the reason is a test
that only holds because of where it ran.

## The class that can move a survivor, and it is not the timing split above

A mutant inside a static initializer is covered by no captured test, so the tool
runs it against the WHOLE suite. Its wall clock is therefore the suite's, which
is the longest any mutant has, and it is the first class to cross
`additional-timeout` when the machine is busy - and a survivor that times out is
counted as killed, so this class DOES move the set. Two mutants in
`RuleRefusalTable`'s table did exactly that on a clone while an unrelated build
was running beside the measurement.

How much of the engine is in that class is derived rather than written here:

```
node -e "
const fs=require('node:fs');
const r=JSON.parse(fs.readFileSync(process.argv[1],'utf8'));
let scored=0, stat=0, statSurv=0;
for (const d of Object.values(r.files)) for (const m of d.mutants) {
  if (!['Survived','Killed','Timeout','NoCoverage'].includes(m.status)) continue;
  scored++;
  if (m.static) { stat++; if (m.status==='Survived') statSurv++; }
}
console.log('scored',scored,'| static',stat,'| static and surviving',statSurv);
" StrykerOutput/<run>/reports/mutation-report.json
```

The rule vocabulary is built in static tables, so this class is large here rather
than incidental. It is the direction to check first when this job reds on a
survivor the run killed, and it is the reason not to run anything else on the
machine while a measurement is being taken.

## What the record cannot say

Whether a mutant on the `unstable` list is genuinely unstable or was put there to
make a real survivor go away. Nothing in a report separates the two. The entry
carries the verdicts seen and the runs that saw each so a reader has something to
check the claim against, and the review is where a wrong one is caught.

It also says nothing about a target framework it was not taken on. The record is
taken against the `net10.0` build, which is the target framework
`stryker-config.json` names. Nothing in the engine is written per target
framework, which is a reason to expect the two to agree rather than evidence that
they do:

```
grep -rn '#if\|NET9_0\|NET10_0' --include=*.cs Jellyfin.Plugin.SmartCollections.Engine/ ; echo "exit=$?"
exit=1
```

## What is not covered

The plugin host assembly is not mutated. It holds the settings page, the service
registration and the library event plumbing, and those are covered by their own
tests; the determinism claim this measurement exists for lives in the engine.

The run happens on a schedule and on manual dispatch, never on a pull request. A
change that weakens the suite therefore merges and is caught by the next
scheduled run rather than at review time, which is a cost paid on purpose: a full
run at concurrency one is a long time per pull request to say the same thing as
the run before it. `.github/workflows/mutation.yml` is where that is argued.
