#!/usr/bin/env node
// Proves every invariant rule bites, for the reason it names (#16).
//
// A rule that produces zero findings on a clean tree is indistinguishable from
// a rule that produces zero findings on everything. This runs the same rule set
// over a directory of fixtures and holds each rule to three things:
//
//   its bites fixture fires it, and fires nothing else
//   its allows fixture, one token away, fires nothing
//   both fixtures exist
//
// The third is what stops a rule arriving without its proof. Deleting a rule's
// bites fixture is refused here rather than noticed in review.
//
// Node rather than a shell script or a C# test: this repository already runs
// node for the coverage floor, the lint it drives has no .NET binding, and a
// test project that shells out to a downloaded binary is a test that fails for
// a reason that has nothing to do with the plugin.
//
//   node tools/opengrep/check-fixtures.js
//
// Set OPENGREP to the binary if it is not on PATH.

const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "..", "..");
const rulesFile = path.join("tools", "opengrep", "rules.yml");
const fixtures = path.join("tools", "opengrep", "fixtures");
const binary = process.env.OPENGREP || "opengrep";

function fail(message) {
    console.error(message);
    process.exitCode = 1;
}

const declared = fs
    .readFileSync(path.join(root, rulesFile), "utf8")
    .split(/\r?\n/)
    .map((line) => /^\s*-\s+id:\s*(\S+)\s*$/.exec(line))
    .filter(Boolean)
    .map((match) => match[1]);

if (declared.length === 0) {
    fail(`No rule id found in ${rulesFile}.`);
    return;
}

const run = spawnSync(binary, ["scan", "--config", rulesFile, "--json", "--quiet", fixtures], { cwd: root, encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });

if (run.error) {
    fail(`Could not run ${binary}: ${run.error.message}`);
    return;
}

let report;
try {
    report = JSON.parse(run.stdout);
} catch {
    fail(`${binary} did not produce JSON. exit=${run.status}\n${run.stdout}\n${run.stderr}`);
    return;
}

// What fired where, as a set of rule ids per fixture path.
//
// A reported check id carries the config file's path as a namespace, so
// `tools.opengrep.network-client-in-the-engine` is the id `rules.yml` declares
// as `network-client-in-the-engine`. Only the last segment is the rule, and a
// rule id holds no dot, so taking it is unambiguous.
const fired = new Map();
for (const result of report.results || []) {
    const file = path.relative(root, path.resolve(root, result.path));
    const ids = fired.get(file) || new Set();
    ids.add(result.check_id.split(".").pop());
    fired.set(file, ids);
}

const show = (set) => (set && set.size ? [...set].sort().join(", ") : "nothing");

console.log(`Rules declared in ${rulesFile}: ${declared.length}`);

for (const id of declared) {
    const bites = path.join(fixtures, "bites", `${id}.cs`);
    const allows = path.join(fixtures, "allows", `${id}.cs`);

    for (const file of [bites, allows]) {
        if (!fs.existsSync(path.join(root, file))) {
            fail(`${id}: ${file} is missing, so the rule ships without its proof.`);
        }
    }

    const onBites = fired.get(bites);
    const onAllows = fired.get(allows);

    if (!onBites || !onBites.has(id)) {
        fail(`${id}: did not fire on ${bites}, so nothing here proves it bites.`);
    } else if (onBites.size !== 1) {
        fail(`${id}: ${bites} fired ${show(onBites)}, so the fixture proves more than one rule and neither exactly.`);
    }

    if (onAllows && onAllows.size > 0) {
        fail(`${id}: ${allows} fired ${show(onAllows)}, so the rule refuses its own repair.`);
    }

    if (process.exitCode !== 1) {
        console.log(`  ${id}: bites its fixture, passes its near miss`);
    }
}

// A finding on a fixture no rule claims means a fixture was left behind or a
// rule was renamed without its files, and both read as green without this.
const claimed = new Set();
for (const id of declared) {
    claimed.add(path.join(fixtures, "bites", `${id}.cs`));
    claimed.add(path.join(fixtures, "allows", `${id}.cs`));
}
for (const file of fired.keys()) {
    if (!claimed.has(file)) {
        fail(`${file} fired ${show(fired.get(file))} and no rule declares it.`);
    }
}

if (process.exitCode === 1) {
    console.error("The invariant rules are not proven.");
} else {
    console.log("Every rule fires on its own fixture and on no other.");
}
