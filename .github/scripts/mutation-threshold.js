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
// Usage:
//
//   node .github/scripts/mutation-threshold.js <stryker-output-directory> [config-file]

"use strict";

const fs = require("node:fs");
const path = require("node:path");

const outputDir = process.argv[2];
const configFile = process.argv[3] || "stryker-config.json";

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

if (declared < taken) {
    console.error(`::error::The mutation score is ${score.toFixed(2)}% and thresholds.break is left behind at ${declared}. ` + `Write ${taken} as "break" in ${configFile}, so the threshold states the score it was taken from, and record this run in docs/mutation-score.md.`);
    process.exit(1);
}
if (declared > taken) {
    console.error(`::error::thresholds.break is ${declared} in ${configFile} and the mutation score is ${score.toFixed(2)}%, whose whole number is ${taken}. ` + "A threshold above the score reds every run; write the score the suite reaches.");
    process.exit(1);
}

console.log("The threshold is the score this run reached, with its fraction dropped.");
