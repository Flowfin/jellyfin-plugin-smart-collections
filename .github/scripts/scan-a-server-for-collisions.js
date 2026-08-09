// Scan a running server for route, task, identifier and directory collisions (#60).
//
// Two plugins collide in a small number of concrete ways, and every one of them
// is enumerable from a running server rather than argued about. This reads what
// a server reports about itself and returns the collisions it finds, so it says
// the same thing about any set of plugins and gains value as the set grows.
//
// Usage:
//
//   node .github/scripts/scan-a-server-for-collisions.js \
//     --image jellyfin/jellyfin:10.11.11 --package <package.zip>
//   node .github/scripts/scan-a-server-for-collisions.js --base http://127.0.0.1:8096
//   node .github/scripts/scan-a-server-for-collisions.js --prove-the-scan-bites
//
// WHAT IT REPORTS IS A LIST AND NEVER THE FIRST ENTRY. An operator clearing
// collisions wants the whole set, and a scan that stopped at the first would
// turn one afternoon into as many afternoons as there are collisions.
//
// The kinds it reports, and where each one is read from. The list is not
// counted here, because a count in a comment drifts against the code that
// decides it and the probe mode below prints the number it actually ran.
//
//   route                        Two paths in the server's OpenAPI document
//                                that its router answers as one. A JSON object
//                                cannot hold a duplicate key, so a literal
//                                repeat is not what a document can show. What
//                                it can show is two keys ASP.NET routing treats
//                                as the same route, which is a pair differing
//                                only in case or in a trailing slash, and that
//                                is a collision the server resolves either way.
//   scheduled-task-name          Two tasks the dashboard cannot tell apart.
//   scheduled-task-key           Two tasks the server keys the same.
//   plugin-identifier            Two loaded plugins claiming one identifier.
//   plugin-configuration-file    Two loaded plugins whose configuration file
//                                names collide. Both are written into the
//                                server's one configurations directory:
//
//                                  gh api "repos/jellyfin/jellyfin/contents/Emby.Server.Implementations/AppBase/BaseApplicationPaths.cs?ref=v10.11.11" \
//                                    --jq .content | base64 -d | grep -n 'PluginConfigurationsPath'
//                                  61:        public string PluginConfigurationsPath => Path.Combine(PluginsPath, "configurations");
//
//                                so two plugins reporting one name overwrite
//                                each other's settings with no error anywhere.
//   plugin-data-folder           Two loaded plugins whose installed directory
//                                under the server's plugin path is one
//                                directory.
//   plugin-identifier-not-the-manifest
//                                A plugin the server lists under an identifier
//                                its manifest does not declare. This is the one
//                                kind that is not about two plugins, and it is
//                                here because a package on a server that is not
//                                the package this tree describes makes every
//                                other verdict a verdict about something else.
//
// THE DATA FOLDER IS DERIVED AND THE OTHERS ARE REPORTED. No route on either
// supported line returns a loaded plugin's directory on disk; `/Plugins`
// returns a name, a version, an identifier, a configuration file name and a
// status, and nothing else:
//
//   gh api "repos/jellyfin/jellyfin/contents/MediaBrowser.Model/Plugins/PluginInfo.cs?ref=v10.11.11" \
//     --jq .content | base64 -d | grep -nE 'public .* \{ get'
//
// So the directory is derived from the name and the version, which is the pair
// the server's own installer builds the directory name out of and the pair this
// repository's alone harness unpacks a package into. A plugin placed on disk by
// hand can sit in a directory that pair does not predict, and this scan cannot
// see that one. That is the whole of what is claimed for this kind, and the
// entry it reports says `derived` so a reader is not left to assume otherwise.
//
// WHAT THIS DOES NOT COVER, said here rather than left to be discovered. The
// issue also asks that each loaded plugin's identifier match the manifest it
// shipped with. The server reports no manifest, and this repository holds one
// manifest, its own, so that arm is checked for this plugin against
// `build.yaml` and is NOT checked for any other plugin. A second plugin
// shipping an identifier its own manifest does not declare is a collision class
// this scan is blind to.
//
// The means is Node, which this repository already writes its gate scripts in
// and adds no runtime for. It shells out to `docker` and to `unzip` where it is
// asked to boot a server, both of which are forced surfaces this tree already
// depends on rather than dependencies it installs.
//
// It needs no display, no elevated rights and no machine trust store. The
// server answers plain HTTP on a port bound to the loopback address, so nothing
// here trusts a certificate.
//
// THE ADMINISTRATOR ROUTES ARE READ BEFORE FIRST-TIME SETUP, DELIBERATELY. A
// server with no user admits an unauthenticated caller to them, because the
// policy behind them admits the setup wizard, and everything this scan reads is
// something the server reports about itself rather than about anybody's
// library. Completing the wizard first would add four calls that can fail for
// reasons a collision scan is not about. Where a server refuses the read
// instead, this exits non-zero naming the route and the status rather than
// reporting a server with no collisions, because an unread server and a clean
// one must not look the same.
//
// A probe mode proves each kind bites before the run that matters, which is the
// ordering the alone harness, the package check and the invariant lint already
// use:
//
//   --prove-the-scan-bites   run the scan over fabricated server reports. Each
//                            kind gets a report that must produce exactly that
//                            collision and a one-change neighbour that must
//                            produce none. Runs no container.

"use strict";

const { execFileSync, spawnSync } = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

// The identity the manifests declare, read rather than written down twice, so a
// change of identifier cannot leave this scan checking the old one and passing
// for the wrong reason.
const MANIFEST = "build.yaml";

// Where the server publishes the document the route arm reads. Both are tried
// and the one that answered is printed, because the template that decides it is
// a server detail rather than this repository's:
//
//   gh api "repos/jellyfin/jellyfin/contents/Jellyfin.Server/Extensions/ApiApplicationBuilderExtensions.cs?ref=v10.11.11" \
//     --jq .content | base64 -d | grep -n 'RouteTemplate'
//   39:                    c.RouteTemplate = "{documentName}/openapi.json";
const OPENAPI_ROUTES = ["/api-docs/openapi.json", "/openapi.json"];

// Who the server is told is calling. It carries no token, because everything
// read here is something the server reports about itself and a server with no
// user admits that read.
const CLIENT = 'MediaBrowser Client="collision-scan", Device="ci", DeviceId="collision-scan", Version="1.0.0.0"';

function usage() {
    console.error("Usage: node .github/scripts/scan-a-server-for-collisions.js --image <image> [--package <package.zip>] [--port <port>]");
    console.error("       node .github/scripts/scan-a-server-for-collisions.js --base <url>");
    console.error("       node .github/scripts/scan-a-server-for-collisions.js --prove-the-scan-bites");
    process.exit(2);
}

/**
 * Reads one scalar out of a flat manifest.
 *
 * @param {string} manifestPath Path to the manifest.
 * @param {string} key The key at column zero.
 * @returns {string} The value, with surrounding quotes removed.
 */
function scalarOf(manifestPath, key) {
    const lines = fs.readFileSync(manifestPath, "utf8").replace(/\r\n/g, "\n").split("\n");

    for (const line of lines) {
        if (line.startsWith(`${key}:`)) {
            return line
                .slice(key.length + 1)
                .trim()
                .replace(/^"|"$/g, "");
        }
    }

    throw new Error(`${manifestPath} declares no ${key} at column zero. This scan refuses rather than checking against a default, because a default would make the identifier arm true of some other plugin.`);
}

/**
 * Runs a command and returns its output, throwing on a non-zero exit.
 *
 * @param {string} file The executable.
 * @param {string[]} args Its arguments.
 * @returns {string} Standard output.
 */
function run(file, args) {
    return execFileSync(file, args, { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
}

/**
 * Runs a command and returns its result without throwing.
 *
 * @param {string} file The executable.
 * @param {string[]} args Its arguments.
 * @returns {{status: number, stdout: string, stderr: string}} The result.
 */
function attempt(file, args) {
    const result = spawnSync(file, args, { encoding: "utf8" });

    return {
        status: result.status === null ? 1 : result.status,
        stdout: result.stdout || "",
        stderr: result.stderr || (result.error ? String(result.error.message) : ""),
    };
}

/**
 * Sleeps.
 *
 * @param {number} ms Milliseconds.
 * @returns {Promise<void>} A promise that settles after the interval.
 */
function pause(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Writes an identifier in the one form two spellings of it compare equal in.
 *
 * @param {string} value An identifier.
 * @returns {string} The comparable form.
 */
function identifierKey(value) {
    return String(value).replace(/-/g, "").toLowerCase();
}

/**
 * Writes a route in the form the server's router matches it by.
 *
 * ASP.NET routing is case-insensitive and treats a trailing slash as the same
 * route, so two documented paths that reduce to one string here are one route
 * on the server whatever the document shows.
 *
 * @param {string} value A documented path.
 * @returns {string} The comparable form.
 */
function routeKey(value) {
    const trimmed = String(value).replace(/\/+$/, "");

    return (trimmed === "" ? "/" : trimmed).toLowerCase();
}

/**
 * Groups values by a key and returns the groups holding more than one.
 *
 * @param {object[]} rows The rows to group.
 * @param {function(object): (string|undefined)} keyOf The key for a row, or undefined to skip it.
 * @param {function(object): string} nameOf What to call a row in the report.
 * @returns {{key: string, names: string[]}[]} The colliding groups, in key order.
 */
function collisionsBy(rows, keyOf, nameOf) {
    const groups = new Map();

    for (const row of rows) {
        const key = keyOf(row);

        if (key === undefined || key === null || key === "") {
            continue;
        }

        if (!groups.has(key)) {
            groups.set(key, []);
        }

        groups.get(key).push(nameOf(row));
    }

    const found = [];

    for (const [key, names] of groups) {
        if (names.length > 1) {
            found.push({ key, names: names.slice().sort() });
        }
    }

    found.sort((left, right) => (left.key < right.key ? -1 : left.key > right.key ? 1 : 0));
    return found;
}

/**
 * The whole scan, as a function over what a server reported.
 *
 * Kept separate from the calls that fetch the report so the probe mode below
 * runs exactly this and not a second implementation of it.
 *
 * @param {{plugins: object[], tasks: object[], paths: string[]}} report What the server said.
 * @param {{guid: string, name: string}} identity What this repository's manifest declares.
 * @returns {{kind: string, names: string[], detail: string}[]} Every collision found.
 */
function collisionsIn(report, identity) {
    const found = [];
    const plugins = report.plugins || [];
    const tasks = report.tasks || [];
    const paths = report.paths || [];

    for (const group of collisionsBy(
        paths.map((value) => ({ value })),
        (row) => routeKey(row.value),
        (row) => row.value,
    )) {
        found.push({
            kind: "route",
            names: group.names,
            detail: `the server's router answers ${group.names.length} documented paths as ${group.key}, so which handler runs is decided by whichever registration won`,
        });
    }

    for (const group of collisionsBy(
        tasks,
        (task) => task.Name,
        (task) => `${task.Name} (key ${task.Key || "none"})`,
    )) {
        found.push({
            kind: "scheduled-task-name",
            names: group.names,
            detail: `${group.names.length} scheduled tasks are called ${group.key}, so the dashboard cannot tell them apart`,
        });
    }

    for (const group of collisionsBy(
        tasks,
        (task) => task.Key,
        (task) => `${task.Name || "unnamed"} (key ${task.Key})`,
    )) {
        found.push({
            kind: "scheduled-task-key",
            names: group.names,
            detail: `${group.names.length} scheduled tasks are keyed ${group.key}, and the server keys a task's triggers and its last result by that string`,
        });
    }

    for (const group of collisionsBy(
        plugins,
        (plugin) => identifierKey(plugin.Id),
        (plugin) => `${plugin.Name} ${plugin.Version} (${plugin.Id})`,
    )) {
        found.push({
            kind: "plugin-identifier",
            names: group.names,
            detail: `${group.names.length} loaded plugins claim identifier ${group.key}`,
        });
    }

    for (const group of collisionsBy(
        plugins,
        (plugin) => (plugin.ConfigurationFileName || "").toLowerCase(),
        (plugin) => `${plugin.Name} ${plugin.Version} (${plugin.ConfigurationFileName})`,
    )) {
        found.push({
            kind: "plugin-configuration-file",
            names: group.names,
            detail: `${group.names.length} loaded plugins write settings to ${group.key} in the server's one plugin configurations directory`,
        });
    }

    for (const group of collisionsBy(
        plugins,
        (plugin) => `${String(plugin.Name || "").replace(/ /g, "")}_${plugin.Version}`.toLowerCase(),
        (plugin) => `${plugin.Name} ${plugin.Version}`,
    )) {
        found.push({
            kind: "plugin-data-folder",
            names: group.names,
            detail: `${group.names.length} loaded plugins install into ${group.key}, derived from the name and the version rather than reported by the server`,
        });
    }

    // This repository holds one manifest, so this arm is checked for one plugin
    // and the header says so. A plugin the server lists that this repository did
    // not ship has no manifest here to be held against.
    const mine = plugins.filter((plugin) => identifierKey(plugin.Name || "") === identifierKey(identity.name));

    for (const plugin of mine) {
        if (identifierKey(plugin.Id) !== identifierKey(identity.guid)) {
            found.push({
                kind: "plugin-identifier-not-the-manifest",
                names: [`${plugin.Name} ${plugin.Version} (${plugin.Id})`],
                detail: `${MANIFEST} declares ${identity.guid} and the server lists this plugin under ${plugin.Id}, so the package on the server is not the one this tree describes`,
            });
        }
    }

    return found;
}

/**
 * Calls a route and refuses to treat an unread server as a clean one.
 *
 * @param {string} base The server's base address.
 * @param {string} route The route, beginning with a slash.
 * @returns {Promise<object>} The parsed body.
 */
async function read(base, route) {
    const response = await fetch(`${base}${route}`, {
        headers: { Authorization: CLIENT },
    });

    if (response.status !== 200) {
        throw new Error(`${route} answered ${response.status}. A server this scan could not read is not a server with no collisions, so this refuses rather than reporting an empty list.`);
    }

    return JSON.parse(await response.text());
}

/**
 * Collects what the server reports about its plugins, its tasks and its routes.
 *
 * @param {string} base The server's base address.
 * @returns {Promise<{plugins: object[], tasks: object[], paths: string[]}>} The report.
 */
async function reportFrom(base) {
    const plugins = await read(base, "/Plugins");
    const tasks = await read(base, "/ScheduledTasks");

    let document;
    let answered;

    for (const route of OPENAPI_ROUTES) {
        try {
            document = await read(base, route);
            answered = route;
            break;
        } catch {
            document = undefined;
        }
    }

    if (!document) {
        throw new Error(`No route in ${OPENAPI_ROUTES.join(", ")} returned the server's OpenAPI document, so the route arm has nothing to read and this scan cannot report a route collision either way.`);
    }

    const paths = Object.keys(document.paths || {});

    console.log(`  /Plugins:        ${plugins.length} loaded`);
    console.log(`  /ScheduledTasks: ${tasks.length} declared`);
    console.log(`  ${answered}: ${paths.length} documented path(s)`);

    return { plugins, tasks, paths };
}

/**
 * Waits for a route to answer 200.
 *
 * THE ROUTE WAITED ON IS ONE THE SCAN READS, AND THAT IS NOT A REFINEMENT.
 * `/System/Info/Public` answers 200 while the rest of the server is still
 * starting, and a read posted at that moment is held by the middleware that
 * queues requests until startup finishes. Measured on the 10.11 line:
 *
 *   /System/Info/Public answers: {"version":"10.11.11", ..., "startupWizardCompleted":false}
 *   Error: /Plugins answered 503.
 *
 * So the public route says the server is listening and `/Plugins` says it is
 * ready to be read, and the difference between the two is a scan that fails on
 * every run for a reason that has nothing to do with a collision.
 *
 * A route that never reaches 200 is reported with its last status rather than
 * treated as an empty list, which is the same refusal `read` makes and for the
 * same reason.
 *
 * @param {string} base The server's base address.
 * @param {string} route The route to poll.
 * @param {string} container The container name, for the log on failure.
 * @param {number} seconds How long to wait.
 * @returns {Promise<void>} Resolves once the route answers 200.
 */
async function waitFor(base, route, container, seconds) {
    const deadline = Date.now() + seconds * 1000;
    let last = "no attempt completed";

    while (Date.now() < deadline) {
        try {
            const response = await fetch(`${base}${route}`, { headers: { Authorization: CLIENT } });

            if (response.status === 200) {
                console.log(`${route} answers: ${(await response.text()).trim().slice(0, 200)}`);
                return;
            }

            last = `status ${response.status}`;
        } catch (error) {
            last = String(error.message);
        }

        await pause(2000);
    }

    console.error(`The server did not answer ${base}${route} with 200 within ${seconds}s. Last attempt: ${last}`);
    console.error(attempt("docker", ["logs", container]).stdout);
    console.error(attempt("docker", ["logs", container]).stderr);
    throw new Error(`The server never answered ${route}.`);
}

/**
 * Builds a server report out of the parts a probe cares about.
 *
 * @param {object} parts Any of plugins, tasks and paths.
 * @returns {{plugins: object[], tasks: object[], paths: string[]}} A whole report.
 */
function reported(parts) {
    return { plugins: parts.plugins || [], tasks: parts.tasks || [], paths: parts.paths || [] };
}

/**
 * Builds one entry of a plugin list.
 *
 * @param {string} name The plugin name.
 * @param {string} version Its version.
 * @param {string} id Its identifier.
 * @param {string} configuration Its configuration file name.
 * @returns {object} The entry, shaped as the server reports one.
 */
function listedPlugin(name, version, id, configuration) {
    return { Name: name, Version: version, Id: id, ConfigurationFileName: configuration };
}

/**
 * Builds one entry of a scheduled task list.
 *
 * @param {string} name The task name.
 * @param {string} key Its key.
 * @returns {object} The entry, shaped as the server reports one.
 */
function listedTask(name, key) {
    return { Name: name, Key: key };
}

/**
 * The fabricated reports the probe mode runs the scan over.
 *
 * Each entry names the kind it must produce, a report that must produce exactly
 * that one, and a one-change neighbour that must produce none. The neighbour is
 * one character away from the report beside it, for the reason a near miss
 * exists at all: a fixture that could not have passed proves less than one that
 * nearly did. Read each pair by the character that differs.
 *
 * The identifier this repository ships is taken from the manifest rather than
 * written down here, so a change of identifier moves the fixtures with it
 * instead of leaving them proving something about the old one.
 *
 * @param {{guid: string, name: string}} identity What this repository's manifest declares.
 * @returns {{kind: string, bites: object, allows: object}[]} The probes.
 */
function probesFor(identity) {
    const mine = identity.guid;
    const theirs = "9a2f1c04-0000-4000-8000-000000000001";
    const myConfiguration = "Jellyfin.Plugin.SmartCollections.xml";
    const theirConfiguration = "Something.Else.xml";
    const myPlugin = listedPlugin(identity.name, "1.0.0.0", mine, myConfiguration);

    return [
        {
            kind: "scheduled-task-name",
            bites: reported({ tasks: [listedTask("Refresh Smart Collections", "SmartCollectionsRefresh"), listedTask("Refresh Smart Collections", "SomebodyElsesRefresh")] }),
            allows: reported({ tasks: [listedTask("Refresh Smart Collections", "SmartCollectionsRefresh"), listedTask("Refresh Smart Collection", "SomebodyElsesRefresh")] }),
        },
        {
            kind: "scheduled-task-key",
            bites: reported({ tasks: [listedTask("Refresh Smart Collections", "RefreshCollections"), listedTask("Rebuild playlists", "RefreshCollections")] }),
            allows: reported({ tasks: [listedTask("Refresh Smart Collections", "RefreshCollections"), listedTask("Rebuild playlists", "RefreshCollection")] }),
        },
        {
            // The pair differs in case, which is what the server's router folds
            // away. The neighbour adds two characters to the second path, which
            // is what makes them two routes rather than one.
            kind: "route",
            bites: reported({ paths: ["/SmartCollections/Refresh", "/smartcollections/refresh"] }),
            allows: reported({ paths: ["/SmartCollections/Refresh", "/smartcollections/refreshed"] }),
        },
        {
            // The two spellings differ by one dash rather than by a digit,
            // because a server reports an identifier in either form and a scan
            // comparing the strings as written would call these two plugins.
            kind: "plugin-identifier",
            bites: reported({ plugins: [myPlugin, listedPlugin("Something Else", "2.0.0.0", mine.replace("-", ""), theirConfiguration)] }),
            allows: reported({ plugins: [myPlugin, listedPlugin("Something Else", "2.0.0.0", theirs, theirConfiguration)] }),
        },
        {
            // Differing only in case, because the server writes both into one
            // directory and a filesystem that folds case holds one file.
            kind: "plugin-configuration-file",
            bites: reported({ plugins: [myPlugin, listedPlugin("Something Else", "2.0.0.0", theirs, myConfiguration.toLowerCase())] }),
            allows: reported({ plugins: [myPlugin, listedPlugin("Something Else", "2.0.0.0", theirs, myConfiguration.toLowerCase().replace("collections", "collection"))] }),
        },
        {
            // One name carries a space and the other does not, which is the
            // pair that reduces to one installed directory. The neighbour moves
            // the version by one, which is what puts them in two.
            kind: "plugin-data-folder",
            bites: reported({ plugins: [myPlugin, listedPlugin(identity.name.replace(/ /g, ""), "1.0.0.0", theirs, theirConfiguration)] }),
            allows: reported({ plugins: [myPlugin, listedPlugin(identity.name.replace(/ /g, ""), "1.0.0.1", theirs, theirConfiguration)] }),
        },
        {
            kind: "plugin-identifier-not-the-manifest",
            bites: reported({ plugins: [listedPlugin(identity.name, "1.0.0.0", theirs, myConfiguration)] }),
            allows: reported({ plugins: [myPlugin] }),
        },
    ];
}

/**
 * Runs the scan over the fabricated reports and refuses a scan that does not
 * behave.
 *
 * @param {{guid: string, name: string}} identity What this repository's manifest declares.
 * @returns {number} An exit status.
 */
function proveTheScanBites(identity) {
    const probes = probesFor(identity);
    let failed = 0;

    for (const probe of probes) {
        const bites = collisionsIn(probe.bites, identity);
        const allows = collisionsIn(probe.allows, identity);
        const kinds = bites.map((collision) => collision.kind);

        if (kinds.length !== 1 || kinds[0] !== probe.kind) {
            console.error(`  ${probe.kind}: the report built to collide produced [${kinds.join(", ") || "nothing"}], so this kind is not shown to bite.`);
            failed += 1;
            continue;
        }

        if (allows.length !== 0) {
            console.error(`  ${probe.kind}: the one-change neighbour produced [${allows.map((collision) => collision.kind).join(", ")}], so this kind reports a collision where there is none.`);
            failed += 1;
            continue;
        }

        console.log(`  ${probe.kind}: bites its report, passes its one-change neighbour (${bites[0].names.join(" / ")})`);
    }

    if (failed > 0) {
        console.error("");
        console.error(`${failed} of ${probes.length} kind(s) are not proven, so a run against a real server would report a clean scan it has not earned.`);
        return 1;
    }

    console.log("");
    console.log(`Every one of the ${probes.length} kinds fires on a report built to collide and on no neighbour of it.`);
    return 0;
}

async function main() {
    const argv = process.argv.slice(2);
    const identity = { guid: scalarOf(MANIFEST, "guid"), name: scalarOf(MANIFEST, "name"), version: scalarOf(MANIFEST, "version") };

    if (argv.includes("--prove-the-scan-bites")) {
        console.log(`Running the scan over ${probesFor(identity).length} fabricated server report(s) and their one-change neighbours.`);
        process.exit(proveTheScanBites(identity));
    }

    const valueOf = (flag) => {
        const at = argv.indexOf(flag);
        return at === -1 ? undefined : argv[at + 1];
    };

    const image = valueOf("--image");
    const packaged = valueOf("--package");
    const already = valueOf("--base");
    const port = Number(valueOf("--port") || 18099);

    if (!image && !already) {
        usage();
    }

    console.log(`Plugin:  ${identity.name} ${identity.version} ${identity.guid}`);

    let report;

    if (already) {
        console.log(`Server:  ${already}, already running`);
        report = await reportFrom(already);
    } else {
        const runtime = attempt("docker", ["version", "--format", "{{.Server.Version}}"]);

        if (runtime.status !== 0) {
            console.error("No container runtime answered. This scan needs one and does not start one: a daemon somebody else's session owns is not a thing to switch on in passing.");
            console.error(runtime.stderr.trim() || runtime.stdout.trim());
            process.exit(1);
        }

        console.log(`Runtime: ${runtime.stdout.trim()}`);
        console.log(`Image:   ${image}`);
        console.log(`Package: ${packaged || "none, this scans the server's own surface alone"}`);

        const root = fs.mkdtempSync(path.join(os.tmpdir(), "collision-scan-"));
        const config = path.join(root, "config");
        const plugins = path.join(config, "plugins");
        const container = `collision-scan-${process.pid}`;

        fs.mkdirSync(plugins, { recursive: true });
        fs.mkdirSync(path.join(root, "cache"), { recursive: true });

        if (packaged) {
            const into = path.join(plugins, `${identity.name.replace(/ /g, "")}_${identity.version}`);

            fs.mkdirSync(into, { recursive: true });
            run("unzip", ["-q", "-o", packaged, "-d", into]);

            const unpacked = fs.readdirSync(into);

            console.log(`Unpacked ${unpacked.length} entr(ies) into the server's plugin directory: ${unpacked.join(", ")}`);

            if (unpacked.length === 0) {
                console.error("The package unpacked to nothing, so the scan below would read a server with no plugin and would report the empty list it is meant to earn.");
                process.exit(1);
            }
        }

        try {
            run("docker", ["run", "--detach", "--name", container, "--publish", `127.0.0.1:${port}:8096`, "--volume", `${config}:/config`, "--volume", `${path.join(root, "cache")}:/cache`, image]);

            const base = `http://127.0.0.1:${port}`;

            // Listening first, then readable. The header on waitFor says why
            // these are two waits rather than one.
            await waitFor(base, "/System/Info/Public", container, 240);
            await waitFor(base, "/Plugins", container, 240);

            report = await reportFrom(base);
        } finally {
            attempt("docker", ["rm", "--force", container]);

            // The server writes into the mounted directories as the user inside
            // the container, which is not the user running this, so removing
            // them can fail with a permission error. That is tidying rather than
            // a verdict, and it does not decide the exit status.
            try {
                fs.rmSync(root, { recursive: true, force: true });
            } catch (error) {
                console.log(`Left behind ${root}: ${error.message}`);
            }
        }

        if (packaged && !report.plugins.some((plugin) => identifierKey(plugin.Id) === identifierKey(identity.guid))) {
            console.error("");
            console.error(`The server does not list ${identity.guid}, so the package did not load and an empty collision list would be a statement about a server without this plugin on it.`);
            process.exit(1);
        }
    }

    const found = collisionsIn(report, identity);

    console.log("");

    if (found.length === 0) {
        console.log("No collisions.");
        return;
    }

    console.error(`${found.length} collision(s):`);

    for (const collision of found) {
        console.error(`  [${collision.kind}] ${collision.detail}`);

        for (const name of collision.names) {
            console.error(`    ${name}`);
        }
    }

    process.exit(1);
}

main().catch((error) => {
    console.error(String(error.stack || error.message));
    process.exit(1);
});
