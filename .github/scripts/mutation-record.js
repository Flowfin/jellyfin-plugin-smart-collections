// Hold the mutation gate on a set that does not move, rather than on a score
// that does (#200).
//
// WHAT THIS REPLACES AND WHY. `thresholds.break` and the four figures on
// `docs/mutation-score.md` were both held against the score of whatever run the
// check was handed, and that score is a fact about a run rather than about the
// tree. Measured on this repository: three consecutive runs on an unchanged
// tree, nothing edited between them, landed 91.31, 91.44 and 91.31, and the two
// that agreed to the hundredth disagreed about WHICH mutants survived. So a
// check comparing scores cannot see a set that moved underneath one, and a
// check comparing a score to a threshold reds on an unchanged tree for a reason
// nobody wrote down. #200 carries the measurement; this script is the
// arrangement decided on it.
//
// WHAT IS COMPARED INSTEAD. The record names the surviving mutants by file,
// line, column, mutator and replacement, and the run is compared against that
// set mutant by mutant. A score is a sum over verdicts, so it moves whenever one
// verdict does; a set moves only where a verdict does, and the check can then
// say WHICH one and in which direction. The three numbers it reports are killed,
// survived and unstable.
//
// THE UNSTABLE LIST IS THE PART TO READ CAREFULLY. A mutant that is killed on
// one run and survives on another is not evidence about the suite, it is
// evidence about the run, and counting it on whichever side the last run put it
// is what made the score move. Such a mutant is recorded as unstable, with the
// verdicts that were seen and the runs that saw each, and it is excluded from
// both the survivor comparison and the floor. It is excluded rather than counted
// as a survivor because a survivor is a test somebody should write, and putting
// a timing artefact on that list sends a reader to write a test that already
// exists.
//
// THE FLOOR IS A COUNT OVER THE STABLE SET, not a percentage. A percentage moves
// when the denominator moves, so adding a file to the engine lowers it while
// nothing about the suite changed. A killed count only falls when something the
// suite used to notice stops being noticed, which is the thing worth refusing.
//
// BOTH DIRECTIONS OF THE RELATION ARE REFUSED, the same shape
// `.github/scripts/coverage-floor.js` and the threshold arm before this one
// carried. A survivor the record does not name is a suite that got weaker. A
// record survivor the run killed is a record left behind, and leaving it there
// is slack the next weakening would be absorbed by. A record entry naming a
// mutant the run does not produce at all is a record describing a tree that is
// gone, and it is refused rather than skipped, because a record whose entries
// quietly stop matching is a record that agrees with everything.
//
// THE CONCURRENCY IS PART OF THE ARRANGEMENT AND IS COMPARED. The record was
// taken under one, and the reason the survivor set is stable at all is that the
// coverage capture is not racing itself. A record taken at one concurrency and
// judged against a run at another is two different experiments compared as one,
// so the record declares the number and this script reads it back out of the
// workflow that runs the tool.
//
// The means is a Node script invoked by `node`, for the reason
// `.github/scripts/coverage-floor.js` gives and `mutation-threshold.js` gave
// before it: Node is on every GitHub runner and on any machine that already
// builds this plugin's front-end files, it needs no package installed, and it
// reads the JSON report the run already writes, so it adds no artefact and no
// second source for any number.
//
// It fails closed. No report, several reports, a report holding no mutant, an
// unreadable record, or a record declaring no floor all red rather than passing
// as "nothing to compare". Several reports is a refusal rather than a choice
// between them: a record judged against an unspecified run is the same fault one
// directory up.
//
// WHAT THE PROOF COVERS AND WHAT IT DOES NOT. `--prove-the-check-bites` runs
// every arm of the COMPARISON over a fabricated run and record pair built to
// trip it and a one-change neighbour that trips nothing, and the workflow runs
// that before the tool so a check that stopped refusing is reported in seconds.
// The shape guards - no report, several reports, an empty report, a record
// missing a field, a workflow and a record disagreeing about the concurrency -
// exit the process rather than returning a refusal, so they are outside that
// harness by construction. They were each exercised by hand and the runs are in
// the pull request that landed this file.
//
// WHAT THIS CANNOT SEE. Whether a mutant on the unstable list is genuinely
// unstable or was put there to make a real survivor go away. Nothing in a report
// separates the two, and the review is where that is caught; the record carries
// the runs that saw each verdict so a reader has something to check the claim
// against. It also says nothing about a target framework it was not run on, and
// nothing about a mutant the tool never produced.
//
// Usage:
//
//   node .github/scripts/mutation-record.js <stryker-output-directory> [record-file] [workflow-file]
//   node .github/scripts/mutation-record.js <stryker-output-directory> --print-record
//   node .github/scripts/mutation-record.js --prove-the-check-bites

"use strict";

const fs = require("node:fs");
const path = require("node:path");

const DEFAULT_RECORD = "mutation-record.json";
const DEFAULT_WORKFLOW = ".github/workflows/mutation.yml";

function fail(message) {
    console.error(`error: ${message}`);
    process.exit(1);
}

// A mutant's identity: file, line, column, mutator and replacement, which is
// what the comparison on #200 keyed on and is the narrowest tuple that separates
// two mutants the tool produced at one site.
//
// A TUPLE RATHER THAN AN OBJECT, AND THE REASON IS THE DIFF. The record holds a
// hundred and more of these, and the thing a reviewer has to see is which ones
// moved. One mutant per line makes a new survivor one added line; the same set
// as objects is five lines each and a new survivor is a block a reader has to
// assemble. The positions are named once, in the record's own `columns`, and
// this check refuses a record whose `columns` are not these - so the order can
// be read out of the file rather than remembered, and cannot be changed under a
// reader without the check saying so.
//
// The key is the JSON of that tuple rather than a joined string, because a
// replacement carries braces, quotes and colons and any separator this file
// chose would be one of them.
const COLUMNS = ["file", "line", "column", "mutator", "replacement"];

function entryOf(file, mutant) {
    return [file, mutant.location.start.line, mutant.location.start.column, mutant.mutatorName, mutant.replacement];
}

function keyOf(file, mutant) {
    return JSON.stringify(entryOf(file, mutant));
}

function keyOfEntry(entry) {
    return JSON.stringify(entry);
}

function describe(entry) {
    return `${entry[0]}:${entry[1]}:${entry[2]} ${entry[3]} -> ${JSON.stringify(entry[4])}`;
}

// Sorted so a record written from one run and a record written from another are
// the same bytes where they hold the same set, and a diff of the file is a diff
// of the set rather than of the order the report happened to list its files in.
function sortEntries(entries) {
    return entries.slice().sort((left, right) => {
        for (let at = 0; at < COLUMNS.length; at++) {
            const a = left[at];
            const b = right[at];
            // Line and column compared as numbers, so 60 sorts before 124 the way
            // a reader opening the file expects. Sorting the whole tuple as text
            // puts 124 first, which is deterministic and unreadable.
            if (a === b) {
                continue;
            }
            if (typeof a === "number" && typeof b === "number") {
                return a - b;
            }
            return String(a) < String(b) ? -1 : 1;
        }
        return 0;
    });
}

// Every JSON report under the output directory, at any depth. Stryker writes it
// into a per-run subdirectory whose name is a timestamp, so the path cannot be
// written out here.
function findReports(dir) {
    const found = [];
    let entries;
    try {
        entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch (err) {
        fail(`cannot read the output directory ${dir}: ${err.message}`);
    }
    for (const entry of entries) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            found.push(...findReports(full));
        } else if (entry.name === "mutation-report.json") {
            found.push(full);
        }
    }
    return found;
}

// THE PATH IN THE REPORT IS ABSOLUTE AND THE RECORD MAY NOT CARRY ONE. The tool
// keys each file by its full path on the machine that ran it, so a record
// written here would name `G:\...` and a runner would produce `/home/runner/...`
// - every entry dangling and every mutant new, on a tree nobody touched. The
// report declares the root it mutated, so each key is taken relative to that and
// the separators are normalised to the one a JSON file can carry unescaped.
//
// A key that does not sit under that root is refused rather than kept absolute.
// Only the engine project is mutated, so there is no such file today, and one
// arriving would otherwise be the single entry that silently never matches.
function relativeTo(root, file) {
    const base = root.replace(/\\/g, "/").replace(/\/+$/, "");
    const full = file.replace(/\\/g, "/");
    if (base.length > 0 && full.startsWith(base + "/")) {
        return full.slice(base.length + 1);
    }
    fail(`the report names ${file}, which is not under the project root ${root} it declares, ` + "so it cannot be written into a record as a path any other machine would produce");
    return full;
}

// The report reduced to what this check compares: one status per mutant key, and
// the entry each key was built from so a message can name the mutant rather than
// print its key.
function statusesOf(report) {
    const statuses = new Map();
    for (const [file, data] of Object.entries(report.files)) {
        const name = relativeTo(report.projectRoot, file);
        for (const mutant of data.mutants || []) {
            statuses.set(keyOf(name, mutant), mutant.status);
        }
    }
    return statuses;
}

function entriesOf(report) {
    const entries = new Map();
    for (const [file, data] of Object.entries(report.files)) {
        const name = relativeTo(report.projectRoot, file);
        for (const mutant of data.mutants || []) {
            entries.set(keyOf(name, mutant), entryOf(name, mutant));
        }
    }
    return entries;
}

function readReport(outputDir) {
    const reports = findReports(outputDir);
    if (reports.length === 0) {
        fail(`no mutation-report.json under ${outputDir}. The run produced no report, ` + "which is not the same as a run that measured nothing, so this fails rather than passing.");
    }
    if (reports.length > 1) {
        fail(`${reports.length} mutation reports under ${outputDir}, so which run this record is being judged against is not decided:\n  ` + reports.join("\n  ") + "\nPoint this script at one run's directory.");
    }
    let report;
    try {
        report = JSON.parse(fs.readFileSync(reports[0], "utf8"));
    } catch (err) {
        fail(`cannot read the mutation report ${reports[0]}: ${err.message}`);
    }
    if (!report.files || typeof report.files !== "object") {
        fail(`${reports[0]} declares no "files" object, so it is not a report this check understands`);
    }
    if (typeof report.projectRoot !== "string" || report.projectRoot.length === 0) {
        fail(`${reports[0]} declares no "projectRoot", so its file paths cannot be made relative and a record written from it would only ever match the machine that produced it`);
    }
    const statuses = statusesOf(report);
    if (statuses.size === 0) {
        fail(`${reports[0]} holds no mutant. A run that mutated nothing and a run that mutated everything and was noticed each end with nothing to refuse, and only one of them is a suite doing its job, so this fails rather than comparing an empty set against an empty record.`);
    }
    return { file: reports[0], statuses, entries: entriesOf(report) };
}

// A timeout is a kill, for the reason docs/mutation-score.md gives: a suite that
// never finishes is a suite that noticed. A mutant no test reaches is neither,
// and it is counted with the survivors because it is the same statement about
// the suite - nothing would notice this change.
const KILLED = new Set(["Killed", "Timeout"]);
const SURVIVED = new Set(["Survived", "NoCoverage"]);

/**
 * Compares one run against one record.
 *
 * @param {{statuses: Map<string, string>, entries: Map<string, object>}} run The run.
 * @param {object} record The record.
 * @returns {{problems: string[], killed: number, survived: number, unstable: number}} What it found.
 */
function judge(run, record) {
    const problems = [];

    const recorded = new Map();
    for (const entry of record.survivors) {
        recorded.set(keyOfEntry(entry), entry);
    }
    const unstable = new Map();
    for (const entry of record.unstable) {
        unstable.set(keyOfEntry(entry.mutant), entry);
    }

    // A record entry naming a mutant this run did not produce. The code it sat in
    // moved or went, so the entry describes a tree that is gone. Refused in both
    // lists rather than skipped: an entry that quietly stops matching is an entry
    // that agrees with every run afterwards, which is exactly the slack the
    // record exists to remove.
    for (const [key, entry] of recorded) {
        if (!run.statuses.has(key)) {
            problems.push(`The record names a surviving mutant this run did not produce: ${describe(entry)}. ` + "The code it sat in has moved or gone, so take the entry out in the change that moved it.");
        }
    }
    for (const [key, entry] of unstable) {
        if (!run.statuses.has(key)) {
            problems.push(`The record names an unstable mutant this run did not produce: ${describe(entry.mutant)}. ` + "The code it sat in has moved or gone, so take the entry out in the change that moved it.");
        }
    }

    let killed = 0;
    let survived = 0;
    for (const [key, status] of run.statuses) {
        if (unstable.has(key)) {
            continue;
        }
        if (KILLED.has(status)) {
            killed += 1;
            if (recorded.has(key)) {
                problems.push(`The record lists ${describe(recorded.get(key))} as surviving and this run killed it. ` + "Take it out of the record in the change that killed it; a survivor left in the record is slack the next weakening would be absorbed by.");
            }
            continue;
        }
        if (!SURVIVED.has(status)) {
            continue;
        }
        survived += 1;
        if (!recorded.has(key)) {
            problems.push(`A mutant survived that the record does not name: ${describe(run.entries.get(key))}. ` + "Either write the test that kills it, or add it to the record in the change that produced it, with the reason.");
        }
    }

    if (typeof record.killed !== "number") {
        problems.push(`The record declares no numeric "killed" floor, so there is nothing for this run's ${killed} to be compared against.`);
    } else if (killed < record.killed) {
        problems.push(`This run killed ${killed} mutants over the stable set and the record's floor is ${record.killed}. ` + "The suite notices less than it did; raise the floor only in a change that raises the count.");
    } else if (killed > record.killed) {
        problems.push(`This run killed ${killed} mutants over the stable set and the record's floor is left behind at ${record.killed}. ` + `Write ${killed} as "killed" in the record, so the floor states the count it was taken from.`);
    }

    return { problems, killed, survived, unstable: unstable.size };
}

// The record's own shape, refused before anything is compared. Each field is
// required: a record that stopped carrying one reads as a record that agrees
// with the run, which is the direction this whole script exists against.
function readRecord(recordFile) {
    let record;
    try {
        record = JSON.parse(fs.readFileSync(recordFile, "utf8"));
    } catch (err) {
        fail(`cannot read the record ${recordFile}: ${err.message}`);
    }
    if (!Array.isArray(record.survivors)) {
        fail(`${recordFile} declares no "survivors" array, so it is not a record this check understands`);
    }
    if (!Array.isArray(record.unstable)) {
        fail(`${recordFile} declares no "unstable" array. A record with nothing unstable declares an empty one, ` + "because an absent list and a list that is empty are different statements and only one of them was measured.");
    }
    if (typeof record.concurrency !== "number") {
        fail(`${recordFile} declares no numeric "concurrency", so the arrangement it was taken under is not stated`);
    }
    // The tuple positions, named in the record and compared here rather than
    // trusted. A record whose columns say something else is a record whose rows
    // mean something else, and a comparison that read them positionally anyway
    // would answer confidently about the wrong field.
    if (!Array.isArray(record.columns) || JSON.stringify(record.columns) !== JSON.stringify(COLUMNS)) {
        fail(`${recordFile} declares columns ${JSON.stringify(record.columns)} and this check reads ${JSON.stringify(COLUMNS)}. ` + "Every row is a tuple in that order, so the two have to say the same thing.");
    }
    for (const entry of record.unstable) {
        if (!entry || typeof entry !== "object" || !Array.isArray(entry.mutant) || typeof entry.verdicts !== "object") {
            fail(`${recordFile} carries an unstable entry with no "mutant" tuple and "verdicts", ` + "and a mutant excluded from the comparison without the runs that saw each verdict is an exclusion nobody can check");
        }
    }
    for (const entry of record.survivors) {
        if (!Array.isArray(entry) || entry.length !== COLUMNS.length) {
            fail(`${recordFile} carries a survivor that is not a ${COLUMNS.length}-value tuple: ${JSON.stringify(entry)}`);
        }
    }
    return record;
}

// The arrangement the record was taken under, read back out of the workflow that
// runs the tool. A record taken at concurrency one and judged against a run at
// the runner's default is two experiments compared as one, and nothing else in
// this tree would say so.
function judgeConcurrency(workflowFile, record) {
    let workflow;
    try {
        workflow = fs.readFileSync(workflowFile, "utf8").replace(/\r\n/g, "\n");
    } catch (err) {
        fail(`cannot read the workflow ${workflowFile}: ${err.message}`);
    }
    const hit = workflow.match(/^ *run: dotnet stryker --concurrency (\d+)$/m);
    if (!hit) {
        return [`${workflowFile} carries no "run: dotnet stryker --concurrency <n>" line, so the run that judges this record does not state its concurrency. ` + `The record was taken at ${record.concurrency}.`];
    }
    if (Number(hit[1]) !== record.concurrency) {
        return [`${workflowFile} runs the tool at concurrency ${hit[1]} and the record was taken at ${record.concurrency}. ` + "A survivor set is stable only under the arrangement it was measured in, so the two are one number in two places and have to agree."];
    }
    return [];
}

// The record this run implies, printed for a maintainer to write. The unstable
// list is carried over from the record that exists rather than derived, because
// one run cannot see instability: it takes at least two verdicts on one mutant,
// and the runs that saw each are what the entry has to name.
function printRecord(run, existing) {
    const unstable = existing ? existing.unstable : [];
    const excluded = new Set(unstable.map((entry) => keyOfEntry(entry.mutant)));
    const survivors = [];
    let killed = 0;
    for (const [key, status] of run.statuses) {
        if (excluded.has(key)) {
            continue;
        }
        if (KILLED.has(status)) {
            killed += 1;
        } else if (SURVIVED.has(status)) {
            survivors.push(run.entries.get(key));
        }
    }
    console.log(JSON.stringify({ concurrency: existing ? existing.concurrency : 1, columns: COLUMNS, killed, survivors: sortEntries(survivors), unstable }, null, 4));
}

// ---------------------------------------------------------------------------
// The proof that the check bites.
//
// Every arm above is exercised here against a fabricated run and record pair
// built to trip exactly it, and against a one-change neighbour that trips
// nothing. A near miss that could not have failed proves less than one that
// nearly did, so each neighbour is the smallest edit that makes the refusal
// wrong: the survivor is in the record, the killed mutant is out of it, the
// floor is the count.
// ---------------------------------------------------------------------------

function at(file, line) {
    return [file, line, 1, "Block removal mutation", "{}"];
}

function fabricate(mutants) {
    const statuses = new Map();
    const entries = new Map();
    for (const mutant of mutants) {
        const entry = at(mutant.file, mutant.line);
        const key = keyOfEntry(entry);
        statuses.set(key, mutant.status);
        entries.set(key, entry);
    }
    return { statuses, entries };
}

function probes() {
    return [
        {
            kind: "a survivor the record does not name",
            bites: {
                run: fabricate([{ file: "A.cs", line: 1, status: "Survived" }]),
                record: { concurrency: 1, killed: 0, survivors: [], unstable: [] },
            },
            allows: {
                run: fabricate([{ file: "A.cs", line: 1, status: "Survived" }]),
                record: { concurrency: 1, killed: 0, survivors: [at("A.cs", 1)], unstable: [] },
            },
        },
        {
            kind: "a record survivor the run killed",
            bites: {
                run: fabricate([{ file: "A.cs", line: 1, status: "Killed" }]),
                record: { concurrency: 1, killed: 1, survivors: [at("A.cs", 1)], unstable: [] },
            },
            allows: {
                run: fabricate([{ file: "A.cs", line: 1, status: "Killed" }]),
                record: { concurrency: 1, killed: 1, survivors: [], unstable: [] },
            },
        },
        {
            kind: "a record entry the run did not produce",
            bites: {
                run: fabricate([{ file: "A.cs", line: 1, status: "Killed" }]),
                record: { concurrency: 1, killed: 1, survivors: [at("Gone.cs", 9)], unstable: [] },
            },
            allows: {
                run: fabricate([{ file: "A.cs", line: 1, status: "Killed" }]),
                record: { concurrency: 1, killed: 1, survivors: [], unstable: [] },
            },
        },
        {
            kind: "an unstable entry the run did not produce",
            bites: {
                run: fabricate([{ file: "A.cs", line: 1, status: "Killed" }]),
                record: { concurrency: 1, killed: 1, survivors: [], unstable: [{ mutant: at("Gone.cs", 9), verdicts: { Killed: ["run 1"], Survived: ["run 2"] } }] },
            },
            allows: {
                run: fabricate([
                    { file: "A.cs", line: 1, status: "Killed" },
                    { file: "Gone.cs", line: 9, status: "Killed" },
                ]),
                record: { concurrency: 1, killed: 1, survivors: [], unstable: [{ mutant: at("Gone.cs", 9), verdicts: { Killed: ["run 1"], Survived: ["run 2"] } }] },
            },
        },
        {
            kind: "a floor above the count",
            bites: {
                run: fabricate([{ file: "A.cs", line: 1, status: "Killed" }]),
                record: { concurrency: 1, killed: 2, survivors: [], unstable: [] },
            },
            allows: {
                run: fabricate([{ file: "A.cs", line: 1, status: "Killed" }]),
                record: { concurrency: 1, killed: 1, survivors: [], unstable: [] },
            },
        },
        {
            kind: "a floor left behind the count",
            bites: {
                run: fabricate([
                    { file: "A.cs", line: 1, status: "Killed" },
                    { file: "B.cs", line: 2, status: "Killed" },
                ]),
                record: { concurrency: 1, killed: 1, survivors: [], unstable: [] },
            },
            allows: {
                run: fabricate([
                    { file: "A.cs", line: 1, status: "Killed" },
                    { file: "B.cs", line: 2, status: "Killed" },
                ]),
                record: { concurrency: 1, killed: 2, survivors: [], unstable: [] },
            },
        },
        {
            // The arm that says the unstable list does what it is for. A mutant on
            // it is judged on neither verdict, and the neighbour is the same run
            // with the entry taken off the list, which then trips the survivor arm
            // - so the exclusion is a decision rather than a hole nothing notices.
            kind: "an unstable mutant is judged on neither verdict",
            bites: {
                run: fabricate([{ file: "A.cs", line: 1, status: "Survived" }]),
                record: { concurrency: 1, killed: 0, survivors: [], unstable: [] },
            },
            allows: {
                run: fabricate([{ file: "A.cs", line: 1, status: "Survived" }]),
                record: { concurrency: 1, killed: 0, survivors: [], unstable: [{ mutant: at("A.cs", 1), verdicts: { Killed: ["run 1"], Survived: ["run 2", "run 3"] } }] },
            },
        },
    ];
}

function proveTheCheckBites() {
    const all = probes();
    let failed = 0;

    for (const probe of all) {
        const bites = judge(probe.bites.run, probe.bites.record).problems;
        const allows = judge(probe.allows.run, probe.allows.record).problems;

        if (bites.length === 0) {
            console.error(`  ${probe.kind}: the pair built to fail produced no refusal, so this arm is not shown to bite.`);
            failed += 1;
            continue;
        }
        if (allows.length !== 0) {
            console.error(`  ${probe.kind}: the one-change neighbour produced ${allows.length} refusal(s), the first being "${allows[0]}", so this arm refuses work it should pass.`);
            failed += 1;
            continue;
        }
        console.log(`  ${probe.kind}: bites its pair, passes its one-change neighbour`);
    }

    if (failed > 0) {
        console.error("");
        console.error(`${failed} of ${all.length} arm(s) are not proven, so a green run of this check would be a verdict it has not earned.`);
        return 1;
    }

    console.log("");
    console.log(`Every one of the ${all.length} arms fires on a pair built to fail and on no neighbour of it.`);
    return 0;
}

function main() {
    const argv = process.argv.slice(2);

    if (argv.includes("--prove-the-check-bites")) {
        console.log(`Running the comparison over ${probes().length} fabricated run and record pair(s) and their one-change neighbours.`);
        process.exit(proveTheCheckBites());
    }

    const positional = argv.filter((arg) => !arg.startsWith("--"));
    const outputDir = positional[0];
    if (!outputDir) {
        fail("Usage: node .github/scripts/mutation-record.js <stryker-output-directory> [record-file] [workflow-file]");
    }
    const recordFile = positional[1] || DEFAULT_RECORD;
    const workflowFile = positional[2] || DEFAULT_WORKFLOW;

    const run = readReport(outputDir);

    if (argv.includes("--print-record")) {
        printRecord(run, fs.existsSync(recordFile) ? readRecord(recordFile) : null);
        return;
    }

    const record = readRecord(recordFile);
    const verdict = judge(run, record);
    const problems = verdict.problems.concat(judgeConcurrency(workflowFile, record));

    console.log(`Report read:\n  ${run.file}`);
    console.log(`Record read:\n  ${recordFile}`);
    console.log(`Killed: ${verdict.killed}  Survived: ${verdict.survived}  Unstable: ${verdict.unstable}`);
    console.log(`Floor: ${record.killed} killed over the stable set, at concurrency ${record.concurrency}`);

    if (problems.length > 0) {
        for (const problem of problems) {
            console.error(`::error::${problem}`);
        }
        console.error("");
        console.error(`node ${process.argv[1]} ${outputDir} ${recordFile} --print-record prints the record this run implies.`);
        process.exit(1);
    }

    console.log("The surviving set is the one the record names, and the killed count over the stable set is the floor.");
}

main();
