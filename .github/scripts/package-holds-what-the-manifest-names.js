// Refuse a package that does not carry an assembly its manifest names (#12).
//
// The manifest is a promise to the catalogue: a server installs the zip and
// then loads the files listed under `artifacts`. Nothing between the manifest
// and the zip has ever checked that the promise was kept, so an assembly named
// in the manifest and not produced by the build ships as a package that
// installs cleanly and fails on the server, at load time, in front of a user.
//
// The suite already asserts that the manifest names exactly the assemblies the
// two projects compile to, in ManifestArtifactTests. That reads the project
// files. This reads THE ZIP THAT WILL BE PUBLISHED, which is a different claim:
// a build that succeeded and packaged the wrong output passes the first and
// fails this one.
//
// The listing arrives on standard input rather than being read from the archive
// here, so this script needs no zip library and the repository gains no
// dependency for one comparison. `unzip -Z1` prints one entry per line, which
// is the whole contract.
//
// A directory entry inside the zip is compared by its file name, because JPRM
// has packed flat and nested layouts at different times and which one is in
// force is not this check's business. What it asserts is that a file of that
// name is somewhere in the package.
//
// Usage:
//
//   unzip -Z1 <package.zip> | node \
//     .github/scripts/package-holds-what-the-manifest-names.js <manifest>

"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { artifactsOf } = require("./manifest-artifacts.js");

const manifest = process.argv[2];

if (!manifest) {
    console.error("Usage: unzip -Z1 <package.zip> | node " + ".github/scripts/package-holds-what-the-manifest-names.js <manifest>");
    process.exit(2);
}

const entries = fs
    .readFileSync(0, "utf8")
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0);

if (entries.length === 0) {
    console.error("The package listing on standard input is empty. An empty listing would " + "make every comparison below vacuous, so it is refused here.");
    process.exit(1);
}

const names = new Set(entries.map((entry) => path.basename(entry)));
const listed = artifactsOf(manifest);
const missing = listed.filter((name) => !names.has(name));

console.log(`Manifest:          ${manifest}`);
console.log(`Names:             ${listed.join(", ")}`);
console.log(`Package holds:     ${entries.length} entries`);

if (missing.length > 0) {
    console.error("");
    console.error(`${manifest} names ${missing.length} file(s) the package does not carry:`);
    for (const name of missing) {
        console.error(`  ${name}`);
    }
    console.error("");
    console.error("The package contains:");
    for (const entry of entries) {
        console.error(`  ${entry}`);
    }
    console.error("");
    console.error("Either the build does not produce that assembly, or the manifest names " + "one it should not. A package published in this state installs and then " + "fails on the server when the missing file is loaded.");
    process.exit(1);
}

console.log("");
console.log("Every artefact the manifest names is in the package.");
