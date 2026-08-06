# Security policy

## Reporting a vulnerability

Report privately through GitHub, at
[Report a vulnerability](https://github.com/iderex/jellyfin-plugin-smart-collections/security/advisories/new).
Private reporting is enabled on this repository, so that form works for anyone
with a GitHub account and the report is visible only to the maintainer until it
is resolved.

Do not open a public issue for something with a security dimension. A public
issue is a disclosure.

What helps a report land:

- the plugin version and the Jellyfin server version and line
- the rule document involved, if one is
- what an attacker gets, in one sentence, and who has to be able to do what to
  get it

Expect an acknowledgement. There is one maintainer, so there is no rota and no
guaranteed response window, and saying so is more use than a number nobody is
on the hook for.

## Which versions get fixes

The plugin has not had a release. Once it has, this section will name the
versions that receive fixes. Until then the answer is the default branch and
nothing else.

## What this plugin does and does not do

This is the standing scope statement. It is written so that a reader can tell,
without reading the source, which classes of attack are excluded by design
rather than by care.

The rule engine makes no network calls. A rule document cannot cause the plugin
to fetch anything, from anywhere.

The rule engine executes no code from a rule document. A rule is data that is
validated against a declared field table and a closed operator set. There is no
expression to evaluate, no script host and no path where a document becomes
something that runs.

The rule engine compiles no operator-supplied regular expression. This is the
reason the rule language has no regular expression operator at all: a pattern
supplied by an operator and evaluated on a server task thread can be made to run
for an unbounded time, and there is no per-rule budget that makes that safe on
somebody else's server.

Rule documents are read from the plugin's own data directory, at the path the
server hands out. The plugin does not read rules from anywhere else.

Writing a rule document requires administrator rights on the server, because the
configuration surface is an administrator surface. Anyone who can write a rule
can already do more damage through the server itself, so the threat this policy
is about is not the administrator. It is what a library item's metadata, which
arrives from outside, can make the engine do.

These statements describe the design. Where the code does not yet match one of
them, that is a defect and a report about it is in scope for this policy.
