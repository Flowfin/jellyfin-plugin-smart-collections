// Hold the mutation threshold at the score the run just reached (#190).
//
// `dotnet stryker` asks one question about `thresholds.break`: is the score
// below it. A score above it is the banner and nothing else, so a suite that
// gets better leaves the threshold where it was, and the number goes on
// describing a tree the run has passed until somebody re-runs the tool by hand
// and compares the two. That is the fault #188 refused for the coverage floor,
// in the second place this repository holds a measured number, and this script
// is the same arm on this side of it.
//
// Both directions are refused here for one relation: `break` is the score with
// its fraction dropped. A threshold under that is a record that has stopped
// describing the tree it sits in AND slack the run will not notice - 148
// mutants were scored on the run that opened #190, so one is worth 0.68 points
// and a gap of three whole points is six deleted assertions the tool would have
// stayed green through. A threshold above the whole number below the score is
// refused for the opposite reason, and the tool's own break fires first
// whenever it is above the score itself.
//
// The means is a Node script invoked by `node`, the same command locally and in
// CI, for the reason `.github/scripts/coverage-floor.js` gives: Node is on every
// GitHub runner and on any machine that already builds this plugin's front-end
// files, it needs no package installed, and this tree already carries
// first-party gate steps written in it. It reads the JSON report the run
// already writes and the config the tool already reads, so it adds no artefact
// and no second source for either number.
//
// What this script does NOT do: recompute what the tool decided. It derives the
// score the way `docs/mutation-score.md` derives it, from the mutant statuses in
// the report, because that is the figure that page records and the figure a
// reader compares against the threshold. A mutant that hung the tests counts as
// killed, for the reason that page gives.
//
// It fails closed. No report, several reports, a report holding no mutant, or a
// config declaring no break all red rather than passing as "nothing to compare".
// Several reports is a refusal rather than a choice between them: a threshold
// judged against an unspecified run is the same fault one directory up.
//
// The page is judged beside the threshold (#192). `docs/mutation-score.md` is
// where the score, the status counts and the killed-over-scored figure are
// written down, it is what `docs/testing.md` sends a reader to, and until this
// arm nothing re-extracted any of them: a change that raised the threshold
// correctly and left the page alone passed every route here. It had already
// happened once, and it was found by running the tool rather than by any route.
//
// WHAT THIS ARM CANNOT SEE IS THE PROSE AROUND THE FIGURES. The page argues
// about what a whole-number threshold costs and about what the gap between the
// score and the break buys back, in sentences that carry numbers derived from
// the four this checks. A sentence that has gone stale around a correct figure
// is a judgement about meaning, and no reading of the report makes it. The
// review is where that is caught. The same is true of the paragraph in
// `.github/workflows/mutation.yml`, which is outside this arm because #192
// names the page.
//
// Usage:
//
//   node .github/scripts/mutation-threshold.js <stryker-output-directory> [config-file] [page-file]

"use strict";

const fs = require("node:fs");
const path = require("node:path");

const outputDir = process.argv[2];
const configFile = process.argv[3] || "stryker-config.json";
const pageFile = process.argv[4] || "docs/mutation-score.md";

function fail(message) {
    console.error(`error: ${message}`);
    process.exit(1);
}

if (!outputDir) {
    fail("Usage: node .github/scripts/mutation-threshold.js <stryker-output-directory> [config-file]");
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

const reports = findReports(outputDir);
if (reports.length === 0) {
    fail(`no mutation-report.json under ${outputDir}. The run produced no report, ` + "which is not the same as a run that measured nothing, so this fails rather than passing.");
}
if (reports.length > 1) {
    fail(`${reports.length} mutation reports under ${outputDir}, so which run this threshold is being judged against is not decided:\n  ` + reports.join("\n  ") + "\nPoint this script at one run's directory.");
}

const reportFile = reports[0];
let report;
try {
    report = JSON.parse(fs.readFileSync(reportFile, "utf8"));
} catch (err) {
    fail(`cannot read the mutation report ${reportFile}: ${err.message}`);
}
if (!report.files || typeof report.files !== "object") {
    fail(`${reportFile} declares no "files" object, so it is not a report this check understands`);
}

// The statuses, counted the way docs/mutation-score.md counts them. A timeout
// is a kill: a suite that never finishes is a suite that noticed.
const counts = {};
for (const entry of Object.values(report.files)) {
    for (const mutant of entry.mutants || []) {
        counts[mutant.status] = (counts[mutant.status] || 0) + 1;
    }
}
const killed = (counts.Killed || 0) + (counts.Timeout || 0);
const scored = killed + (counts.Survived || 0) + (counts.NoCoverage || 0);
if (scored === 0) {
    fail(`${reportFile} holds no scored mutant, so there is no score to compare a threshold against`);
}
const score = (100 * killed) / scored;

let config;
try {
    config = JSON.parse(fs.readFileSync(configFile, "utf8"));
} catch (err) {
    fail(`cannot read the configuration ${configFile}: ${err.message}`);
}
const thresholds = config["stryker-config"] && config["stryker-config"].thresholds;
if (!thresholds || typeof thresholds.break !== "number") {
    fail(`${configFile} declares no numeric "stryker-config".thresholds.break`);
}
const declared = thresholds.break;

// Defensive rather than load-bearing, and this comment says which. A score that
// is exactly a whole number divides exactly here - IEEE division of two integers
// returns the correctly rounded result, which for an integer result is that
// integer - so 41 killed of 50 scored floors to 82 without any slack, and a
// fixture holds that case. The slack is here for a future score that arrives
// from an arithmetic with a rounding step in it, and it costs nothing today.
const EPSILON = 1e-9;
const taken = Math.floor(score + EPSILON);

console.log(`Report read:\n  ${reportFile}`);
console.log(`Mutants by status: ${JSON.stringify(counts)}`);
console.log(`Score: ${killed}/${scored} = ${score.toFixed(2)}%`);
console.log(`Threshold: break ${declared}, the score with its fraction dropped is ${taken}`);

// Every refusal below goes into one list and the run exits once, so a reader
// who has let both the threshold and the page go stale is told both times
// rather than being sent back for the second after repairing the first.
const problems = [];

if (declared < taken) {
    problems.push(`The mutation score is ${score.toFixed(2)}% and thresholds.break is left behind at ${declared}. ` + `Write ${taken} as "break" in ${configFile}, so the threshold states the score it was taken from.`);
}
if (declared > taken) {
    problems.push(`thresholds.break is ${declared} in ${configFile} and the mutation score is ${score.toFixed(2)}%, whose whole number is ${taken}. ` + "A threshold above the score reds every run; write the score the suite reaches.");
}

// The page beside the threshold (#192). Four figures are re-extracted from it
// and compared against this run. Each shape is required: a page that stopped
// carrying one reads as a page that agrees, which is the direction this whole
// script exists against.
//
// The status counts are compared as a set of name-to-number pairs rather than as
// the rendered line, because the order the statuses come out in follows the
// order the report happens to list mutants and is not a fact about the tree. The
// repair is printed as the line to write, so the reader is handed the text
// rather than the difference.
//
// A status with no mutants is absent from the printed object rather than zero,
// because the page pastes what the reader above it prints. So a page writing
// `Timeout: 0` against a run that timed nothing out is refused, and the message
// hands over the line to write rather than leaving the reader to guess which
// spelling is meant.
//
// The two historical lines further down the page, which record a killed-and-
// timed-out split measured on the tree of 2026-08-17, match none of these shapes
// and are left alone on purpose: they are kept as the reading they were.
let page;
try {
    // Read with the carriage returns removed, so a clone that materialises CRLF
    // and a runner that materialises LF meet the same shapes.
    page = fs.readFileSync(pageFile, "utf8").replace(/\r\n/g, "\n");
} catch (err) {
    fail(`cannot read the page ${pageFile}: ${err.message}`);
}

function figure(pattern, what) {
    const hit = page.match(pattern);
    if (!hit) {
        problems.push(`${pageFile} carries no ${what} this check can read. A page that stopped carrying a figure reads as one that agrees with the run.`);
        return null;
    }
    return hit;
}

const scored2 = score.toFixed(2);
const headline = figure(/^(\d+\.\d{2}) %, and the threshold that holds it is (\d+)\.$/m, "headline score sentence");
if (headline && (headline[1] !== scored2 || Number(headline[2]) !== declared)) {
    problems.push(`${pageFile} says "${headline[0]}" and this run scored ${scored2}% against a break of ${declared}. ` + `Write "${scored2} %, and the threshold that holds it is ${declared}."`);
}

const banner = figure(/^\[\.\.\] The final mutation score is (\d+\.\d{2}) %$/m, "pasted tool banner");
if (banner && banner[1] !== scored2) {
    problems.push(`${pageFile} pastes a banner reading ${banner[1]} % and this run scored ${scored2}%. ` + `Write "[..] The final mutation score is ${scored2} %".`);
}

const countsLine = figure(/^\{ ([^}\n]*) \}$/m, "status count line");
if (countsLine) {
    const written = {};
    for (const pair of countsLine[1].split(",")) {
        const hit = pair.trim().match(/^([A-Za-z]+): (\d+)$/);
        if (hit) {
            written[hit[1]] = Number(hit[2]);
        }
    }
    const line =
        "{ " +
        Object.entries(counts)
            .map(([k, v]) => `${k}: ${v}`)
            .join(", ") +
        " }";
    const same = Object.keys(counts).length === Object.keys(written).length && Object.entries(counts).every(([k, v]) => written[k] === v);
    if (!same) {
        problems.push(`${pageFile} records the status counts ${countsLine[0]} and this run produced ${line}. ` + "Write the run's line; the order the statuses appear in follows the report and is not compared.");
    }
}

const ratio = figure(/^(\d+)\/(\d+) (\d+\.\d{2})$/m, "killed-over-scored line");
if (ratio && (Number(ratio[1]) !== killed || Number(ratio[2]) !== scored || ratio[3] !== scored2)) {
    problems.push(`${pageFile} records "${ratio[0]}" and this run produced "${killed}/${scored} ${scored2}". Write the run's figures.`);
}

if (problems.length > 0) {
    for (const problem of problems) {
        console.error(`::error::${problem}`);
    }
    process.exit(1);
}

console.log(`The threshold is the score this run reached, with its fraction dropped, and ${pageFile} records this run.`);
