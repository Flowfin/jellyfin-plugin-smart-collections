# The mutation score, and where its number comes from

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
dotnet stryker
```

Everything the run needs is in `stryker-config.json` and the tool version is
pinned in `.config/dotnet-tools.json`, so a clone reproduces the number rather
than a nearby one. Reports land under `StrykerOutput/`, which is untracked.

## The number

82.67 %, and the threshold that holds it is 82.

```
dotnet stryker
[..] The final mutation score is 82.67 %
```

Read off the report of the same run rather than off the banner alone:

```
node -e "
const r=require('./StrykerOutput/<run>/reports/mutation-report.json');
const by={};
for(const v of Object.values(r.files)) for(const m of v.mutants) by[m.status]=(by[m.status]||0)+1;
const killed=(by.Killed||0)+(by.Timeout||0);
const scored=killed+(by.Survived||0)+(by.NoCoverage||0);
console.log(by, killed+'/'+scored, (100*killed/scored).toFixed(2));
"
{ Ignored: 21, Killed: 61, Survived: 12, Timeout: 1, CompileError: 36, NoCoverage: 1 }
62/75 82.67
```

Measured on Windows against the net10.0 build, which is the target framework
`stryker-config.json` names. Only that one was measured: the machine this was run
on has no .NET 9 runtime. Nothing here claims the two legs agree, and nothing in
the engine is written per target framework, which is a reason to expect agreement
rather than evidence of it:

```
grep -rn '#if\|NET9_0\|NET10_0' --include=*.cs Jellyfin.Plugin.SmartCollections.Engine/ ; echo "exit=$?"
exit=1
```

## Why the threshold is 82 and not 83 or 80

A threshold is measured, never chosen. A round number above the score reds every
run from the first one, and a round number below it lets the suite weaken by
however wide the gap was without anything saying so.

`thresholds.break` takes a whole number, and 82 is the measured score with its
fraction dropped. It is not a margin: 75 mutants are scored, so one killed mutant
turning into a survivor takes the score to 81.33, which is below the break and
reds the run. The gap between 82.67 and 82 buys nothing back.

Raise it in the same change that raises the score, the way a coverage floor is
raised in `coverage-floors.json`.

## What the split between killed and timed out does not mean

A mutant that hangs the tests is counted as killed, because a test suite that
never finishes is a test suite that noticed. Which of the two a given mutant
lands in moves between runs on the same tree, since it depends on how long the
test host took that time. The sum does not move, and the sum is what the score is
built from. Two runs on the same tree, minutes apart:

```
Killed: 62, Timeout: 0  ->  62/75  ->  82.67 %
Killed: 61, Timeout: 1  ->  62/75  ->  82.67 %
```

`additional-timeout` in `stryker-config.json` is 30000 for this reason. Without
it a slow test host start is read as a hung mutant, which does not move the score
but does move a dozen mutants into a column where they look like something worth
investigating.

## What is not covered

The plugin host assembly is not mutated. It holds the settings page, the service
registration and the library event plumbing, and those are covered by their own
tests; the determinism claim this measurement exists for lives in the engine.

Twelve mutants survive and one is reached by no test at all. The report names
each one with its file and its line, and that list is the thing to work from
rather than the total: raising the score by writing a test for a surviving mutant
is the point, and raising it by deleting the code a mutant sits in is also a
legitimate answer.

The score is measured on a schedule and on manual dispatch, never on a pull
request. A change that weakens the suite therefore merges and is caught by the
next scheduled run rather than at review time, which is a cost paid on purpose:
a full run is minutes of compute per pull request to say the same thing as the
run before it. `.github/workflows/mutation.yml` is where that is argued.
