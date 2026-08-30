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

91.97 %, and the threshold that holds it is 91.

```
dotnet stryker
[..] The final mutation score is 91.97 %
```

Read off the report of the same run rather than off the banner alone:

```
node -e "
const r=require('./StrykerOutput/<run>/reports/mutation-report.json');
const by={};
for(const v of Object.values(r.files)) for(const m of v.mutants) by[m.status]=(by[m.status]||0)+1;
const killed=(by.Killed||0)+(by.Timeout||0);
const scored=killed+(by.Survived||0)+(by.NoCoverage||0);
console.log('{ '+Object.entries(by).map(([k,v])=>k+': '+v).join(', ')+' }');
console.log(killed+'/'+scored, (100*killed/scored).toFixed(2));
"
{ Ignored: 81, Killed: 376, Survived: 32, CompileError: 100, NoCoverage: 1, Timeout: 2 }
378/411 91.97
```

THE COMMAND ABOVE USED TO HAND THE OBJECT STRAIGHT TO `console.log` AND ITS
OUTPUT STOPPED FITTING ON ONE LINE. Node wraps an object once its rendering
passes a width, so a run producing a sixth status printed six lines where this
page had one, and the paste stopped reproducing for a reason that has nothing to
do with the tree. The line is built here instead, which makes the output a
function of the counts rather than of how many of them there are.

Measured on Windows against the net10.0 build, which is the target framework
`stryker-config.json` names. Only that one was measured: the machine this was run
on has no .NET 9 runtime. Nothing here claims the two legs agree, and nothing in
the engine is written per target framework, which is a reason to expect agreement
rather than evidence of it:

```
grep -rn '#if\|NET9_0\|NET10_0' --include=*.cs Jellyfin.Plugin.SmartCollections.Engine/ ; echo "exit=$?"
exit=1
```

## Why the threshold is 91 and not 92 or 88

A threshold is measured, never chosen. A round number above the score reds every
run from the first one, and a round number below it lets the suite weaken by
however wide the gap was without anything saying so.

`thresholds.break` takes a whole number, and 91 is the measured score with its
fraction dropped. WHAT THE DROPPED FRACTION BUYS BACK GREW WITH THE POPULATION,
AND THIS PARAGRAPH USED TO PUT IT AT ONE MUTANT. 411 mutants are scored now
rather than 148, so one is worth 0.24 points against a dropped fraction of 0.97:
losing one takes the score to 91.73, and it takes four to red the run at 90.99.
Counted off the report the figures above are read from.

So dropping the fraction now buys back four mutants where it once bought back
one, and that is what a whole number costs on a larger population rather than
something this file chose. It is also the number to watch: the same slack that
was worth 0.81 points is worth 0.97 now, and it will go on growing with the
mutant count while the threshold stays a whole number. The alternative is a
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

IT HAPPENED AGAIN AND THE MECHANISM CAUGHT IT, WHICH IS THE DIFFERENCE. Three
changes added the value types, the operator set and the composition reader to the
engine, each raised its coverage floors in the same change and none of them
re-ran this tool, so the threshold sat at 85 while the suite reached 91.97. #199
is where that was measured and repaired. Nothing found it before the repair
either: the run that judges it is scheduled rather than on a pull request, so the
gap stood on the default branch from the first of those three merges until
somebody ran the tool by hand.

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

The run recorded above for the current tree lands 376 killed and 2 timed out, so
the split is no longer empty on this side of it and the two columns above are the
reading that predicted it.

## A larger movement between two runs, which the split above does not cover

THE SPLIT IS NOT THE ONLY THING THAT MOVES, AND THIS SECTION IS THE MEASUREMENT
THAT SHOWED IT RATHER THAN AN ARGUMENT ABOUT IT. Two runs on this tree, minutes
apart, with nothing edited between them but `thresholds` in
`stryker-config.json`, which decides no test:

```
[..] The final mutation score is 91.97 %   Killed: 376  Survived: 32  Timeout: 2
[..] The final mutation score is 92.94 %   Killed: 380  Survived: 28  Timeout: 2
```

Four mutants moved from survived to killed. That is not the killed-and-timed-out
split, which leaves the sum alone; it changes the score. The four, read off the
two reports rather than off the banners:

```
Survived -> Killed  Rules/RuleOperatorRow.cs:27      Block removal mutation
Survived -> Killed  Rules/RuleOperatorTable.cs:135   Statement mutation
Survived -> Killed  Rules/RuleOperatorTable.cs:187   String mutation
Survived -> Killed  Rules/RuleOperatorTable.cs:215   String mutation
```

Each of those four sits under a test that asserts it: the null check at 135 is
`ALookupWithNoNameIsRefusedAtTheCall`, and the two message strings are asserted
whole by `AnUnknownNameIsRefusedWithEveryLegalOne` and
`AnOperatorAppliedToATypeItDoesNotAcceptIsRefusedNamingBoth`. So the runs
disagree about which tests reached them rather than about what the tests assert,
and the tool runs in `CoverageBasedTest` mode, which picks the tests for a mutant
out of a coverage capture rather than running all of them. Why that capture
attributed those four differently on two runs is NOT ESTABLISHED HERE, and the
plausible reasons - a cold test host on a first run, tests executing in parallel
during the capture - are guesses this page does not assert.

WHAT IT COSTS IS THE MECHANISM ABOVE, NOT THE NUMBER. The score is a fact about
a run rather than about the tree, and both the threshold and the four figures on
this page are compared against whatever run the check is handed. The lower of the
two is recorded here, so the tool's own break cannot fire on the other; the check
that compares the page against a fresh run has no such margin and reds whenever a
run lands on the other figure. That is a live gap rather than a repaired one.

`additional-timeout` in `stryker-config.json` is 30000 for this reason. Without
it a slow test host start is read as a hung mutant, which does not move the score
but does move a dozen mutants into a column where they look like something worth
investigating.

## What is not covered

The plugin host assembly is not mutated. It holds the settings page, the service
registration and the library event plumbing, and those are covered by their own
tests; the determinism claim this measurement exists for lives in the engine.

Thirty-two mutants survive and one is reached by no test at all. The report names
each one with its file and its line, and that list is the thing to work from
rather than the total: raising the score by writing a test for a surviving mutant
is the point, and raising it by deleting the code a mutant sits in is also a
legitimate answer.

The score is measured on a schedule and on manual dispatch, never on a pull
request. A change that weakens the suite therefore merges and is caught by the
next scheduled run rather than at review time, which is a cost paid on purpose:
a full run is minutes of compute per pull request to say the same thing as the
run before it. `.github/workflows/mutation.yml` is where that is argued.
