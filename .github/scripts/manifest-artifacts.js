// Read the artefact names a shipping manifest lists (#12).
//
// The `artifacts` block of build.yaml is what the catalogue installs, and two
// gate steps have to compare something against it: the package check, which
// asks whether the zip carries each one, and the SBOM check, which asks whether
// the bill of materials describes each one. Both read the list from here rather
// than each carrying its own reader, because two readers of one file disagree
// the day the file is reformatted and the disagreement shows up as a missing
// artefact rather than as a parsing difference.
//
// This reads one block of a flat manifest and nothing else. It is not a YAML
// parser and it is not trying to be: adding a YAML dependency to a repository
// that carries none, for one list of file names, is a runtime and a supply
// chain nobody here needs. The same shape is read by
// Jellyfin.Plugin.SmartCollections.Tests/ManifestArtifactTests.cs, which asserts
// the list matches the assemblies the build produces; what this file adds is the
// same list to a step that runs outside the suite.
//
// An entry's indentation is not read, for the reason that test gives: YAML
// admits a sequence at the key's own column and indented under it, both mean
// the same list, and which one the file carries is the formatter's choice.
//
// It fails closed. A manifest whose `artifacts` block this reader cannot find,
// or finds empty, throws rather than returning nothing, so a check built on it
// reds instead of passing as "nothing to compare".
//
// Usage, on its own:
//
//   node .github/scripts/manifest-artifacts.js build.yaml

"use strict";

const fs = require("node:fs");

/**
 * Reads the artefact names a manifest lists.
 *
 * @param {string} manifestPath Path to the manifest.
 * @returns {string[]} The names, in the order the manifest lists them.
 */
function artifactsOf(manifestPath) {
    const lines = fs.readFileSync(manifestPath, "utf8").replace(/\r\n/g, "\n").split("\n");

    const listed = [];
    let inBlock = false;

    for (const line of lines) {
        if (line.startsWith("artifacts:")) {
            inBlock = true;
            continue;
        }

        if (!inBlock) {
            continue;
        }

        const entry = line.replace(/^\s+/, "");

        if (!entry.startsWith("- ")) {
            break;
        }

        listed.push(entry.slice(2).trim().replace(/^"|"$/g, ""));
    }

    if (!inBlock) {
        throw new Error(`${manifestPath} has no artifacts: block at column zero. This reader ` + "refuses rather than reporting an empty list, because an empty list " + "would let every check built on it pass without comparing anything.");
    }

    if (listed.length === 0) {
        throw new Error(`${manifestPath} lists no artifacts. A package carrying no assembly ` + "installs and does nothing, so this is refused here rather than " + "discovered on a server.");
    }

    return listed;
}

module.exports = { artifactsOf };

if (require.main === module) {
    const manifest = process.argv[2];

    if (!manifest) {
        console.error("Usage: node .github/scripts/manifest-artifacts.js <manifest>");
        process.exit(2);
    }

    for (const name of artifactsOf(manifest)) {
        console.log(name);
    }
}
