// Hold each assembly's coverage at the score the suite actually reaches (#71).
//
// The shared test workflow this repository calls collects no coverage, so a
// change that adds a branch no test reaches is green and stays green. Read what
// that workflow runs rather than trusting this sentence:
//
//   gh api repos/jellyfin/jellyfin-meta-plugins/contents/.github/workflows/test.yaml \
//     --jq .content | base64 -d
//
// The floors are per assembly and they live in coverage-floors.json. A single
// repository-wide number is refused: once the solution is split into an engine,
// a host and a test project, one combined figure lets engine coverage fall
// while the total holds steady on the host's pages and controllers.
//
// A floor is compared in BOTH directions (#188). A score under its floor is a
// suite that weakened; a score over its floor is an entry that has stopped
// describing the tree it sits in, which is the fault this file exists against
// read the other way round. Both are refused, and a floor with no score and a
// score with no floor are refused beside them.
//
// The means is a Node script invoked by `node`, and it is the same command
// locally and in CI, so the check is reproducible without a runner. Node is on
// every GitHub runner and on any machine that already builds this plugin's
// front-end files, it needs no package installed, and the repository already
// carries one first-party gate step written in it. The floors file is JSON for
// the same reason: it is the only structured format this script can read with
// nothing added to the tree.
//
// What this script does NOT do: parse XML in general. It reads the `<package>`
// start tags of a Cobertura report and nothing else, because that is where the
// per-assembly rates are. It fails closed if it finds no `<packages>` element
// or no package inside one, so a report in a shape it does not understand reds
// the check instead of passing as "nothing to compare".
//
// Usage:
//
//   node .github/scripts/coverage-floor.js <results-directory> [floors-file]

"use strict";

const fs = require("node:fs");
const path = require("node:path");

const resultsDir = process.argv[2];
const floorsFile = process.argv[3] || "coverage-floors.json";

if (!resultsDir) {
    fail("Usage: node .github/scripts/coverage-floor.js <results-directory> [floors-file]");
}

function fail(message) {
    console.error(`error: ${message}`);
    process.exit(1);
}

// Every report under the results directory, at any depth. `dotnet test` writes
// coverage.cobertura.xml into a per-run subdirectory whose name is a guid, so
// the path cannot be written out here.
function findReports(dir) {
    const found = [];
    let entries;
    try {
        entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch (err) {
        fail(`cannot read the results directory ${dir}: ${err.message}`);
    }
    for (const entry of entries) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            found.push(...findReports(full));
        } else if (entry.name.endsWith(".cobertura.xml")) {
            found.push(full);
        }
    }
    return found;
}

// The rates on a `<package>` start tag. Cobertura writes them as fractions of
// one; this returns percentages, which is what the floors file holds.
function readPackages(file) {
    const xml = fs.readFileSync(file, "utf8");
    if (!xml.includes("<packages")) {
        fail(`${file} holds no <packages> element, so it is not a report this check understands`);
    }
    const packages = [];
    for (const match of xml.matchAll(/<package\s([^>]*)>/g)) {
        const attributes = match[1];
        const read = (name) => {
            const hit = attributes.match(new RegExp(`\\b${name}="([^"]*)"`));
            return hit ? hit[1] : null;
        };
        const name = read("name");
        const lineRate = read("line-rate");
        const branchRate = read("branch-rate");
        if (name === null || lineRate === null || branchRate === null) {
            fail(`${file} holds a <package> tag missing name, line-rate or branch-rate`);
        }
        packages.push({
            name,
            line: Number(lineRate) * 100,
            branch: Number(branchRate) * 100,
            file,
        });
    }
    if (packages.length === 0) {
        fail(`${file} declares <packages> and holds no <package> inside it`);
    }
    return packages;
}

const reports = findReports(resultsDir);
if (reports.length === 0) {
    fail(`no *.cobertura.xml under ${resultsDir}. The test run collected no coverage, ` + "which is not the same as coverage being zero, so this fails rather than passing.");
}

let floors;
try {
    floors = JSON.parse(fs.readFileSync(floorsFile, "utf8"));
} catch (err) {
    fail(`cannot read the floors file ${floorsFile}: ${err.message}`);
}
const declared = floors.assemblies;
if (!declared || typeof declared !== "object") {
    fail(`${floorsFile} declares no "assemblies" object`);
}

// One entry per assembly. A package appearing in two reports keeps the lower
// score, because the floor is about the worst the suite reaches and not the
// best.
const measured = new Map();
for (const file of reports) {
    for (const pkg of readPackages(file)) {
        const seen = measured.get(pkg.name);
        if (!seen || pkg.line < seen.line || pkg.branch < seen.branch) {
            measured.set(pkg.name, {
                name: pkg.name,
                line: seen ? Math.min(seen.line, pkg.line) : pkg.line,
                branch: seen ? Math.min(seen.branch, pkg.branch) : pkg.branch,
            });
        }
    }
}

const problems = [];
const rows = [];

// Float noise only. A floor is recorded rounded down, so a measurement that
// genuinely sits on its floor compares equal or above without this slack.
const EPSILON = 1e-9;

for (const [name, score] of [...measured].sort()) {
    const floor = declared[name];
    if (!floor) {
        problems.push(`${name} is measured and coverage-floors.json declares no floor for it. ` + "Add one at the score the suite reaches, so the next change cannot lower it silently.");
        rows.push(`  ${name}: line ${fmt(score.line)}%, branch ${fmt(score.branch)}%, NO FLOOR DECLARED`);
        continue;
    }
    const lineShort = score.line + EPSILON < floor.line;
    const branchShort = score.branch + EPSILON < floor.branch;
    if (lineShort) {
        problems.push(`${name} line coverage is ${fmt(score.line)}% and its floor is ${fmt(floor.line)}%.`);
    }
    if (branchShort) {
        problems.push(`${name} branch coverage is ${fmt(score.branch)}% and its floor is ${fmt(floor.branch)}%.`);
    }
    // A floor left under the score it was taken from (#188). The floors file says of
    // itself that a floor is measured rather than chosen and is raised in the
    // same change that raises the coverage, and until this arm nothing held
    // either sentence: a score above its floor was the row and nothing else, so
    // an entry went on naming a figure the suite had passed. It is refused
    // rather than reported for the reason the two absences around it are: an
    // entry that is wrong is quoted back as though it were right, in a file
    // whose whole purpose is to hold a number somebody measured.
    //
    // What the message has to carry to be affordable is the number to write, so
    // it names the assembly, both figures and the file. That number is the
    // printed row, which is exact while the report keeps a rate to four decimal
    // places - the same bound the floors file's own note states, and the same
    // repair if a collector starts writing a fifth.
    const lineOver = score.line - EPSILON > floor.line;
    const branchOver = score.branch - EPSILON > floor.branch;
    if (lineOver) {
        problems.push(`${name} line coverage is ${fmt(score.line)}% and its floor is left behind at ${fmt(floor.line)}%. ` + `Write ${fmt(score.line)} as the "line" floor for ${name} in ${floorsFile}, so the entry states the score it was taken from.`);
    }
    if (branchOver) {
        problems.push(`${name} branch coverage is ${fmt(score.branch)}% and its floor is left behind at ${fmt(floor.branch)}%. ` + `Write ${fmt(score.branch)} as the "branch" floor for ${name} in ${floorsFile}, so the entry states the score it was taken from.`);
    }
    const mark = lineShort || branchShort ? "  BELOW FLOOR" : lineOver || branchOver ? "  FLOOR LEFT BEHIND" : "";
    rows.push(`  ${name}: line ${fmt(score.line)}% (floor ${fmt(floor.line)}%), ` + `branch ${fmt(score.branch)}% (floor ${fmt(floor.branch)}%)` + mark);
}

// A floor naming an assembly nothing measured is the other direction of the
// same fault: it reads as a held floor and holds nothing.
for (const name of Object.keys(declared)) {
    if (!measured.has(name)) {
        problems.push(`coverage-floors.json declares a floor for ${name} and no report measured it. ` + "Either the assembly is gone and the entry goes with it, or the run stopped covering it.");
    }
}

function fmt(value) {
    return value.toFixed(2);
}

console.log(`Reports read (${reports.length}):`);
for (const file of reports) {
    console.log(`  ${file}`);
}
console.log("Coverage against the recorded floors:");
for (const row of rows) {
    console.log(row);
}

if (problems.length > 0) {
    for (const problem of problems) {
        console.error(`::error::${problem}`);
    }
    process.exit(1);
}

console.log("Every measured assembly is at the floor its entry records.");
