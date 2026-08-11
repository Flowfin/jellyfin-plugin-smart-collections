// Boot a server on one supported line with the packaged plugin and nothing else (#59).
//
// The first half of the interoperability rule is that the plugin works alone.
// Nothing in this repository has ever started a server, so every claim about
// what happens when this plugin is installed has been a claim about source.
// A build proves the code compiles. It does not prove the zip a user downloads
// unpacks into a directory the server reads, that the server loads what it
// finds there, or that the plugin's surface answers an administrator and
// refuses everyone else.
//
// THE PACKAGED ZIP IS THE SUBJECT, NOT THE BUILD OUTPUT. Copying `bin/` into a
// container proves the code works and not that the thing users install works,
// and the difference between those two has been the whole failure in more than
// one plugin release. So this takes an archive produced by the same packager
// the shipping check runs, and unpacks it the way a server does.
//
// Usage:
//
//   node .github/scripts/boot-a-line-with-only-this-plugin.js \
//     --image jellyfin/jellyfin:10.11.11 --package <package.zip>
//
// Three assertions, which are the three the issue names:
//
//   1. The startup log holds no error from this plugin.
//   2. The plugin is listed as loaded and active.
//   3. The plugin's surface answers an administrator and refuses an anonymous
//      caller.
//
// WHAT THE THIRD ASSERTION REACHES IS NARROWER THAN THE ISSUE'S WORDS, and the
// gap is stated here rather than left for a reader to assume. This plugin
// declares no controller of its own yet:
//
//   grep -rn 'ApiController\|HttpGet\|HttpPost' --include=*.cs \
//     Jellyfin.Plugin.SmartCollections/
//
// prints nothing. The endpoints this asserts against are the server's own
// plugin surface keyed on THIS plugin's identifier - the plugin list and this
// plugin's configuration - so what is proved is that the identifier is reachable
// for an administrator and refused for an anonymous caller. An endpoint this
// plugin ships is #47, and when it exists it belongs in the same assertion.
//
// THE ANONYMOUS ARM IS MEANINGLESS BEFORE FIRST-TIME SETUP IS COMPLETED. A
// fresh Jellyfin admits an unauthenticated caller to administrator endpoints
// while no user exists, because the policy behind them admits first-time setup.
// A harness that skipped the wizard would find every endpoint open and could
// read that as either arm of assertion 3. So the wizard is completed before
// anything is asserted, and that ordering is a property of the harness rather
// than a convenience.
//
// The means is Node, which this repository already runs in three gate scripts
// and adds no runtime for. It shells out to `docker` and to `unzip`, both of
// which are forced surfaces: a container runtime is what the issue asks for,
// and `unzip` is already how the package check reads an archive. Neither is a
// dependency this tree installs.
//
// It needs no display, no elevated rights and no machine trust store. The
// server answers plain HTTP on a port bound to the loopback address, so nothing
// here trusts a certificate.
//
// Two probe modes exist so the assertions are proved to bite before the run
// that matters, which is the ordering the package check and the invariant lint
// already use:
//
//   --without-the-plugin       boot the same image with an empty plugin
//                              directory. Exits zero only where the harness
//                              refused it for being UNLISTED, and non-zero for
//                              any other outcome including a harness that died
//                              before it asserted anything.
//   --prove-the-log-scan-bites run the log scan over a fabricated error line
//                              naming this plugin, which it must refuse, and
//                              over one naming something else, which it must
//                              pass. Runs no container.
//
// THE NEAR MISS READS THE SHAPE OF THE FAILURE RATHER THAN THE EXIT STATUS, and
// that is not a refinement. The first run of this file died at first-time setup
// on both lines, non-zero, having asserted nothing at all, and a probe reading
// only the exit status would have recorded that as a proof.
//
// WHAT IS NOT PROVED TO BITE IS THE ANONYMOUS ARM OF ASSERTION 3. Making a
// server answer an anonymous caller on an administrator endpoint would mean
// misconfiguring the server, and a harness that arranges the failure it then
// detects proves the arrangement. That arm is watched passing and has not been
// watched failing, and this sentence is the whole of what is claimed about it.
//
// The administrator arm of assertion 3 is proved, and by the near miss rather
// than by anything written for it. `/Plugins/{id}/Configuration` answers 404 to
// an administrator on a server where this plugin is not installed, and 200
// where it is, on both lines:
//
//   3. /Plugins/70dd8311-1053-4eb7-8406-8df766b2d040/Configuration: administrator 404, anonymous 401   # no plugin
//   3. /Plugins/70dd8311-1053-4eb7-8406-8df766b2d040/Configuration: administrator 200, anonymous 401   # installed
//
// so that route tracks this plugin's presence rather than answering the same
// thing either way, which is what a route asserted against has to do.

"use strict";

const { execFileSync, spawnSync } = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

// The identity the manifests declare. Read from build.yaml rather than written
// down twice, so a change of identifier cannot leave this harness asserting
// against the old one and passing for the wrong reason.
const MANIFEST = "build.yaml";

const CLIENT = 'MediaBrowser Client="alone-harness", Device="ci", DeviceId="alone-harness", Version="1.0.0.0"';

const ADMIN = "harness-administrator";
const SECRET = "9f2c41b7-alone-harness";

// The near miss checks for THIS failure rather than for a non-zero exit.
//
// A step that only asks whether the harness exited non-zero accepts a harness
// that died before it asserted anything, which is not a hypothetical: the first
// run of this file failed at first-time setup on both lines, and a probe reading
// only the exit status would have called that a proof that the plugin-is-loaded
// assertion bites.
const NOT_LISTED = "[not-listed]";

function usage() {
    console.error("Usage: node .github/scripts/boot-a-line-with-only-this-plugin.js --image <image> --package <package.zip> [--port <port>] [--without-the-plugin]");
    console.error("       node .github/scripts/boot-a-line-with-only-this-plugin.js --prove-the-log-scan-bites");
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

    throw new Error(`${manifestPath} declares no ${key} at column zero. This harness refuses rather than asserting against a default, because a default would make every assertion below true of some other plugin.`);
}

/**
 * Decides whether a startup log carries an error from this plugin.
 *
 * The scan is deliberately narrow. A server log holds errors that have nothing
 * to do with any plugin - a missing hardware encoder, an unreachable metadata
 * provider - and a harness that refused the whole log would report this plugin
 * broken for somebody else's reason and would be turned off within a week.
 *
 * @param {string} log The container's combined output.
 * @param {string} name The plugin name the manifest declares.
 * @returns {string[]} The offending lines, empty where there are none.
 */
function pluginErrorsIn(log, name) {
    const named = [name, name.replace(/ /g, ""), "Jellyfin.Plugin.SmartCollections"];

    return log
        .replace(/\r\n/g, "\n")
        .split("\n")
        .filter((line) => /\[(ERR|FTL)\]/.test(line))
        .filter((line) => named.some((token) => line.includes(token)));
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
 * Calls the server.
 *
 * @param {string} base The server's base address.
 * @param {string} route The route, beginning with a slash.
 * @param {object} options Method, body and token.
 * @returns {Promise<{status: number, text: string}>} The status and body.
 */
async function call(base, route, options = {}) {
    const headers = { Authorization: CLIENT };

    if (options.token) {
        headers.Authorization = `${CLIENT}, Token="${options.token}"`;
    }

    if (options.body !== undefined) {
        headers["Content-Type"] = "application/json";
    }

    const response = await fetch(`${base}${route}`, {
        method: options.method || "GET",
        headers,
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
    });

    return { status: response.status, text: await response.text() };
}

/**
 * Waits for a route to answer with the status that means the server is ready
 * for the step after it.
 *
 * TWO ROUTES ARE WAITED ON RATHER THAN ONE, and the reason is measured rather
 * than supposed. `/System/Info/Public` answers 200 while the rest of the server
 * is still starting, and a first-time setup step posted at that moment is
 * refused with 503 by the middleware that holds requests until startup
 * finishes. Both supported lines were watched doing it:
 *
 *   The server answers: {"version":"10.11.11", ..., "startupWizardCompleted":false}
 *     POST /Startup/Configuration -> 503
 *
 * So the public route says the server is listening and the wizard route says it
 * is ready, and the difference between those two is a harness that fails on
 * every run for a reason that has nothing to do with the plugin.
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
            const answer = await call(base, route);

            if (answer.status === 200) {
                console.log(`${route} answers: ${answer.text.trim().slice(0, 240)}`);
                return;
            }

            last = `status ${answer.status}`;
        } catch (error) {
            last = String(error.message);
        }

        await pause(2000);
    }

    console.error(`The server did not answer ${base}${route} with 200 within ${seconds}s. Last attempt: ${last}`);
    console.error("Container output follows.");
    console.error(attempt("docker", ["logs", container]).stdout);
    console.error(attempt("docker", ["logs", container]).stderr);
    throw new Error(`The server never answered ${route}.`);
}

/**
 * Completes first-time setup, so that an anonymous caller is refused for the
 * reason this harness asserts rather than admitted by the setup policy.
 *
 * THE GET ON /Startup/User IS NOT A READ. It is what creates the first user,
 * and posting without it answers 404 on both supported lines, which is what
 * this harness watched happen. The server's own source is the authority:
 *
 *   curl -s "https://api.github.com/repos/jellyfin/jellyfin/contents/Jellyfin.Api/Controllers/StartupController.cs?ref=v10.11.11" \
 *     -H "Accept: application/vnd.github.raw" | sed -n '107,140p'
 *
 * `GetFirstUser` calls `InitializeAsync` and carries a comment asking for the
 * method to be removed when the wizard no longer needs an existing user, and
 * `UpdateStartupUser` returns `NotFound()` where there is none. So the order is
 * a property of the server rather than a preference here, and the same two
 * methods sit in the same order on `v12.0-rc5`, which is the tag the 12.0 job
 * boots. Re-run the command above with that ref to see it.
 *
 * @param {string} base The server's base address.
 * @returns {Promise<string>} An administrator access token.
 */
async function completeFirstTimeSetup(base) {
    const created = await call(base, "/Startup/User");

    console.log(`  GET /Startup/User -> ${created.status} ${created.text.trim().slice(0, 120)}`);

    if (created.status !== 200) {
        throw new Error(`The server did not create a first user: /Startup/User answered ${created.status}. Every step below would fail on a server with no user, and the assertion that an anonymous caller is refused would pass for want of anyone to authenticate.`);
    }

    const steps = [
        ["/Startup/Configuration", { UICulture: "en-US", MetadataCountryCode: "US", PreferredMetadataLanguage: "en" }],
        ["/Startup/User", { Name: ADMIN, Password: SECRET }],
        ["/Startup/RemoteAccess", { EnableRemoteAccess: true, EnableAutomaticPortMapping: false }],
        ["/Startup/Complete", {}],
    ];

    for (const [route, body] of steps) {
        const answer = await call(base, route, { method: "POST", body });

        console.log(`  POST ${route} -> ${answer.status}`);

        if (answer.status >= 400) {
            throw new Error(`First-time setup failed at ${route} with status ${answer.status}: ${answer.text.slice(0, 400)}`);
        }
    }

    const authenticated = await call(base, "/Users/AuthenticateByName", { method: "POST", body: { Username: ADMIN, Pw: SECRET } });

    if (authenticated.status !== 200) {
        throw new Error(`Authenticating the administrator this harness created returned ${authenticated.status}: ${authenticated.text.slice(0, 400)}`);
    }

    const token = JSON.parse(authenticated.text).AccessToken;

    if (!token) {
        throw new Error("The authentication response carried no access token, so every assertion below would run unauthenticated and pass for the wrong reason.");
    }

    return token;
}

/**
 * Compares two identifiers written in either of the forms Jellyfin returns.
 *
 * @param {string} left One identifier.
 * @param {string} right The other.
 * @returns {boolean} Whether they are the same identifier.
 */
function sameIdentifier(left, right) {
    return String(left).replace(/-/g, "").toLowerCase() === String(right).replace(/-/g, "").toLowerCase();
}

/**
 * Runs the three assertions against a server that is already set up.
 *
 * @param {string} base The server's base address.
 * @param {string} token An administrator access token.
 * @param {string} container The container name.
 * @param {{guid: string, name: string}} identity What the manifest declares.
 * @returns {Promise<string[]>} The failures, empty where there are none.
 */
async function assertTheThreeThings(base, token, container, identity) {
    const failures = [];

    // 1. The startup log holds no error from this plugin.
    const logs = attempt("docker", ["logs", container]);
    const output = `${logs.stdout}\n${logs.stderr}`;
    const errors = pluginErrorsIn(output, identity.name);

    console.log(`1. Startup log: ${output.split("\n").length} lines, ${errors.length} naming this plugin at error level.`);

    for (const line of errors) {
        failures.push(`The startup log holds an error from this plugin: ${line.trim()}`);
    }

    // 2. The plugin is listed as loaded and active.
    const listed = await call(base, "/Plugins", { token });

    console.log(`2. GET /Plugins as an administrator -> ${listed.status}`);

    if (listed.status !== 200) {
        failures.push(`GET /Plugins as an administrator returned ${listed.status}, so whether the plugin loaded cannot be read at all.`);
    } else {
        const plugins = JSON.parse(listed.text);
        const mine = plugins.find((plugin) => sameIdentifier(plugin.Id, identity.guid));

        console.log(`   The server lists ${plugins.length} plugin(s): ${plugins.map((plugin) => `${plugin.Name} ${plugin.Version} ${plugin.Status}`).join("; ") || "none"}`);

        if (!mine) {
            failures.push(`${NOT_LISTED} No plugin with identifier ${identity.guid} is listed, so the packaged zip did not load.`);
        } else if (mine.Status !== "Active") {
            failures.push(`The plugin is listed with status ${mine.Status} rather than Active.`);
        }
    }

    // 3. The plugin's surface answers an administrator and refuses an anonymous
    //    caller. Both arms over the same two routes, so a route that answers
    //    nobody cannot pass the second arm by being broken.
    for (const route of ["/Plugins", `/Plugins/${identity.guid}/Configuration`]) {
        const asAdministrator = await call(base, route, { token });
        const asAnonymous = await fetch(`${base}${route}`).then((response) => response.status);

        console.log(`3. ${route}: administrator ${asAdministrator.status}, anonymous ${asAnonymous}`);

        if (asAdministrator.status !== 200) {
            failures.push(`${route} returned ${asAdministrator.status} to an administrator, and 200 is what a reachable plugin surface answers.`);
        }

        if (asAnonymous !== 401 && asAnonymous !== 403) {
            failures.push(`${route} returned ${asAnonymous} to an anonymous caller, and this plugin's surface is administrator-only.`);
        }
    }

    return failures;
}

async function main() {
    const argv = process.argv.slice(2);

    if (argv.includes("--prove-the-log-scan-bites")) {
        const name = scalarOf(MANIFEST, "name");
        const bites = pluginErrorsIn(`[19:12:33] [ERR] [1] Emby.Server.Implementations.Plugins.PluginManager: Failed to load assembly Jellyfin.Plugin.SmartCollections.dll`, name);
        const nearMiss = pluginErrorsIn(`[19:12:33] [ERR] [1] Emby.Server.Implementations.Library.LibraryManager: Error reading a library path`, name);

        if (bites.length !== 1) {
            console.error("The log scan passed a line naming this plugin at error level, so assertion 1 would pass on a server where the plugin failed to load.");
            process.exit(1);
        }

        if (nearMiss.length !== 0) {
            console.error("The log scan refused a line naming something other than this plugin, so assertion 1 would report this plugin broken for somebody else's reason.");
            process.exit(1);
        }

        console.log("The log scan refuses an error naming this plugin and passes one naming something else.");
        return;
    }

    const valueOf = (flag) => {
        const at = argv.indexOf(flag);
        return at === -1 ? undefined : argv[at + 1];
    };

    const image = valueOf("--image");
    const packaged = valueOf("--package");
    const port = Number(valueOf("--port") || 18096);
    const withoutThePlugin = argv.includes("--without-the-plugin");

    if (!image || (!packaged && !withoutThePlugin)) {
        usage();
    }

    const runtime = attempt("docker", ["version", "--format", "{{.Server.Version}}"]);

    if (runtime.status !== 0) {
        console.error("No container runtime answered. This harness needs one and does not start one: a daemon somebody else's session owns is not a thing to switch on in passing.");
        console.error(runtime.stderr.trim() || runtime.stdout.trim());
        process.exit(1);
    }

    console.log(`Container runtime: ${runtime.stdout.trim()}`);

    const identity = { guid: scalarOf(MANIFEST, "guid"), name: scalarOf(MANIFEST, "name"), version: scalarOf(MANIFEST, "version") };

    console.log(`Plugin:            ${identity.name} ${identity.version} ${identity.guid}`);
    console.log(`Image:             ${image}`);
    console.log(`Package:           ${withoutThePlugin ? "none, this is the near miss" : packaged}`);

    const root = fs.mkdtempSync(path.join(os.tmpdir(), "alone-harness-"));
    const config = path.join(root, "config");
    const plugins = path.join(config, "plugins");
    const container = `alone-harness-${process.pid}`;

    fs.mkdirSync(plugins, { recursive: true });
    fs.mkdirSync(path.join(root, "cache"), { recursive: true });

    if (!withoutThePlugin) {
        const into = path.join(plugins, `${identity.name.replace(/ /g, "")}_${identity.version}`);

        fs.mkdirSync(into, { recursive: true });
        run("unzip", ["-q", "-o", packaged, "-d", into]);

        const unpacked = fs.readdirSync(into);

        console.log(`Unpacked ${unpacked.length} entr(ies) into the server's plugin directory: ${unpacked.join(", ")}`);

        if (unpacked.length === 0) {
            console.error("The package unpacked to nothing, so the run below would boot a server with no plugin and could not tell that from a plugin that failed to load.");
            process.exit(1);
        }
    }

    let failures = [];

    try {
        run("docker", ["run", "--detach", "--name", container, "--publish", `127.0.0.1:${port}:8096`, "--volume", `${config}:/config`, "--volume", `${path.join(root, "cache")}:/cache`, image]);

        const base = `http://127.0.0.1:${port}`;

        await waitFor(base, "/System/Info/Public", container, 240);
        await waitFor(base, "/Startup/Configuration", container, 240);

        console.log("Completing first-time setup, so that an anonymous caller is refused by authorisation rather than admitted by the setup policy.");

        const token = await completeFirstTimeSetup(base);

        failures = await assertTheThreeThings(base, token, container, identity);
    } finally {
        attempt("docker", ["rm", "--force", container]);

        // The server writes into the mounted directories as the user inside the
        // container, which is not the user running this, so removing them can
        // fail with a permission error. That is tidying rather than a verdict:
        // it is reported and it does not decide the exit status, because a
        // harness that reported a plugin broken over a leftover temporary file
        // would be turned off within a week.
        try {
            fs.rmSync(root, { recursive: true, force: true });
        } catch (error) {
            console.log(`Left behind ${root}: ${error.message}`);
        }
    }

    if (withoutThePlugin) {
        // The near miss asserts the SHAPE of the failure rather than that there
        // was one, for the reason the constant above gives.
        const refused = failures.filter((failure) => failure.startsWith(NOT_LISTED));

        for (const failure of failures) {
            console.log(`  ${failure}`);
        }

        if (refused.length !== 1) {
            console.error("");
            console.error("The harness was pointed at a server with no plugin installed and did not refuse it for being unlisted. Whatever it reported, the assertion that the plugin is loaded and active has not been shown to bite, so the run that matters would prove nothing.");
            process.exit(1);
        }

        console.log("");
        console.log(`The harness refuses ${image} with no plugin installed, for being unlisted.`);
        return;
    }

    if (failures.length > 0) {
        console.error("");
        console.error(`The plugin does not work alone on ${image}:`);

        for (const failure of failures) {
            console.error(`  ${failure}`);
        }

        process.exit(1);
    }

    console.log("");
    console.log(`The packaged plugin loads and answers on ${image}, alone.`);
}

main().catch((error) => {
    console.error(String(error.stack || error.message));
    process.exit(1);
});
