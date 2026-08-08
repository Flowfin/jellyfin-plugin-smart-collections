// Refuse a bill of materials that does not describe an assembly the package
// ships (#12).
//
// A reader who wants to know which dependencies travel inside the zip should
// not have to unpack it. That is what the SBOM is for, and an SBOM that omits
// one of the two assemblies this plugin ships answers the question wrongly
// while looking complete, which is worse than not publishing one.
//
// What is compared: every name under `artifacts` in the shipping manifest, with
// its `.dll` removed, against the components the SBOM declares. A CycloneDX
// document puts the project it was generated from in `metadata.component` and
// everything that project depends on in `components`, so the two assemblies
// this repository builds land in different places:
//
//   dotnet tool install --global CycloneDX --version 6.2.0
//   dotnet-CycloneDX Jellyfin.Plugin.SmartCollections/Jellyfin.Plugin.SmartCollections.csproj \
//     --framework net10.0 --output sbom --filename sbom.cyclonedx.json \
//     --output-format Json --include-project-references --exclude-test-projects \
//     --set-nuget-purl
//   node -e 'const b=require("./sbom/sbom.cyclonedx.json");
//     console.log(b.metadata.component.name);
//     console.log(b.components.filter(c=>/SmartCollections/.test(c.name)).map(c=>c.name).join())'
//   Jellyfin.Plugin.SmartCollections
//   Jellyfin.Plugin.SmartCollections.Engine
//
// The host assembly is the document's own subject and the engine is a listed
// component, which is why this check accepts a name in either place. Insisting
// the engine appear in `components` and the host in `metadata` would encode the
// generator's current layout into the gate, and the layout is the generator's
// business rather than this repository's.
//
// `--include-project-references` is what puts the engine there at all. Without
// it the SBOM describes the NuGet closure and says nothing about the second
// assembly the package carries, which is exactly the omission this refuses.
//
// It fails closed on shape. An SBOM with no components array, or one this
// script cannot read as JSON, reds rather than passing as "nothing to compare".
//
// Usage:
//
//   node .github/scripts/sbom-holds-what-the-manifest-names.js <manifest> <sbom.json>

"use strict";

const fs = require("node:fs");
const { artifactsOf } = require("./manifest-artifacts.js");

const manifest = process.argv[2];
const sbomPath = process.argv[3];

if (!manifest || !sbomPath) {
    console.error("Usage: node .github/scripts/sbom-holds-what-the-manifest-names.js " + "<manifest> <sbom.json>");
    process.exit(2);
}

let sbom;
try {
    sbom = JSON.parse(fs.readFileSync(sbomPath, "utf8"));
} catch (error) {
    console.error(`${sbomPath} is not readable as JSON: ${error.message}`);
    process.exit(1);
}

if (!Array.isArray(sbom.components)) {
    console.error(`${sbomPath} declares no components array. A document in a shape this ` + "check cannot read is refused rather than passing as an empty comparison.");
    process.exit(1);
}

const declared = new Set();
if (sbom.metadata && sbom.metadata.component && sbom.metadata.component.name) {
    declared.add(sbom.metadata.component.name);
}
for (const component of sbom.components) {
    if (component && component.name) {
        declared.add(component.name);
    }
}

const listed = artifactsOf(manifest);
const expected = listed.map((name) => name.replace(/\.dll$/i, ""));
const missing = expected.filter((name) => !declared.has(name));

console.log(`Manifest:          ${manifest}`);
console.log(`Bill of materials: ${sbomPath}`);
console.log(`Spec version:      ${sbom.specVersion || "not declared"}`);
console.log(`Components:        ${declared.size}`);
console.log(`Names to find:     ${expected.join(", ")}`);

if (missing.length > 0) {
    console.error("");
    console.error(`The bill of materials describes no component for ${missing.length} ` + "assembly the package ships:");
    for (const name of missing) {
        console.error(`  ${name}`);
    }
    console.error("");
    console.error("It declares these instead:");
    for (const name of [...declared].sort()) {
        console.error(`  ${name}`);
    }
    process.exit(1);
}

console.log("");
console.log("Every assembly the manifest names has a component in the SBOM.");
