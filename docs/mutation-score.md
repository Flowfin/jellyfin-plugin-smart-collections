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

85.81 %, and the threshold that holds it is 85.

```
dotnet stryker
[..] The final mutation score is 85.81 %
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
{ Ignored: 33, Killed: 127, Survived: 20, CompileError: 64, NoCoverage: 1 }
127/148 85.81
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

## Why the threshold is 85 and not 86 or 83

A threshold is measured, never chosen. A round number above the score reds every
run from the first one, and a round number below it lets the suite weaken by
however wide the gap was without anything saying so.

`thresholds.break` takes a whole number, and 85 is the measured score with its
fraction dropped. THIS PARAGRAPH SAID THE GAP WAS NOT A MARGIN AND THAT ONE LOST
MUTANT REDS THE RUN. That was true of the reading it was written for, where 75
mutants were scored and one was worth 1.33 points against a fraction of 0.67. It
is not true of this one. 148 mutants are scored, so one is worth 0.68 points
against a dropped fraction of 0.81: losing one takes the score to 85.14, which is
not below 85, and it takes two to red the run at 84.46. Counted off the report
the rates above are read from.

So dropping the fraction buys back at most one mutant, and that is what a whole
number costs rather than something this file chose. The alternative is a
threshold above the score, which reds every run from the first one.

Raise it in the same change that raises the score, the way a coverage floor is
raised in `coverage-floors.json`. That sentence was held by nothing here until
#190, and what it left behind is why the mechanism arrived rather than another
correction: this page read 82.67 with a threshold of 82 while the suite reached
85.81, because the tool asks only whether the score is BELOW the break. A gap of
that width is not the harmless kind either: six killed mutants could have turned
into survivors before the run reddened. `.github/scripts/mutation-threshold.js`
refuses the other direction now, from the report the run already writes, and
`.github/workflows/mutation.yml` is where it runs.

## What the split between killed and timed out does not mean

A mutant that hangs the tests is counted as killed, because a test suite that
never finishes is a test suite that noticed. Which of the two a given mutant
lands in moves between runs on the same tree, since it depends on how long the
test host took that time. The sum does not move, and the sum is what the score is
built from. Two runs on the tree of 2026-08-17, minutes apart, which is the pair
that showed it and is kept as the reading it was rather than restated of a tree
it was not taken on:

```
Killed: 62, Timeout: 0  ->  62/75  ->  82.67 %
Killed: 61, Timeout: 1  ->  62/75  ->  82.67 %
```

Both runs recorded above for the current tree land 127 killed and 0 timed out.

`additional-timeout` in `stryker-config.json` is 30000 for this reason. Without
it a slow test host start is read as a hung mutant, which does not move the score
but does move a dozen mutants into a column where they look like something worth
investigating.

## What is not covered

The plugin host assembly is not mutated. It holds the settings page, the service
registration and the library event plumbing, and those are covered by their own
tests; the determinism claim this measurement exists for lives in the engine.

Twenty mutants survive and one is reached by no test at all. The report names
each one with its file and its line, and that list is the thing to work from
rather than the total: raising the score by writing a test for a surviving mutant
is the point, and raising it by deleting the code a mutant sits in is also a
legitimate answer.

The score is measured on a schedule and on manual dispatch, never on a pull
request. A change that weakens the suite therefore merges and is caught by the
next scheduled run rather than at review time, which is a cost paid on purpose:
a full run is minutes of compute per pull request to say the same thing as the
run before it. `.github/workflows/mutation.yml` is where that is argued.
