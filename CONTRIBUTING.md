# Contributing

This repository is being built to a plan that lives on the tracker. Read the
issue you intend to work on before you write anything, because the issue is
where the evidence and the definition of done are.

## Sign your work

Every commit carries a Developer Certificate of Origin sign-off. The full text
of what you are certifying is in [`DCO`](DCO) at the root of this repository,
unmodified from version 1.1 as published at <https://developercertificate.org/>.

Sign a commit as you make it:

```
git commit -s -m "Say what changed and what failure it prevents"
```

That appends a trailer of the form

```
Signed-off-by: Your Name <you@example.com>
```

The gate compares that trailer against the commit's author name and address
character for character, so `git config user.name` and `git config user.email`
have to hold the identity you want to sign with before you commit.

If you have already made the commits, add the trailer to all of them at once
and force-push your own branch:

```
git rebase --signoff origin/master
```

Commits authored by Dependabot and by GitHub Actions are exempt, because they
cannot sign for themselves. The allowlist is written out rather than inferred:

```
sed -n '/case "$author_email"/,/esac/p' .github/workflows/dco.yml
```

It names those two addresses instead of anything shaped like a bot, so an
author address that merely ends in `[bot]@users.noreply.github.com` is checked
like anyone else. Read the exemption as a convenience for those two and not as
a control. Two of the four patterns accept any prefix, so an address ending in
`+dependabot[bot]@users.noreply.github.com` is exempt whoever chose it, and
git does not verify that the author of a commit is who the address says.

## What runs on a pull request

The workflows that a pull request triggers are the ones declaring a
`pull_request:` event, and the tree is the authority for which those are:

```
grep -rlE '^  pull_request:' .github/workflows/
```

Of those, only some block a merge. The list of blocking checks is a repository
setting rather than a file, so it is read from the API:

```
gh api repos/iderex/jellyfin-plugin-smart-collections/rulesets \
  --jq '.[] | select(.name == "gate") | .id'
gh api repos/iderex/jellyfin-plugin-smart-collections/rulesets/20465770 \
  --jq '[.rules[] | select(.type == "required_status_checks")
         | .parameters.required_status_checks[].context]'
```

Everything a pull request runs that is not in that second list reports its
result and does not stop a merge. Closing the distance between the two lists is
open work on the tracker, in #13 and #20, and this paragraph is a description of
today rather than of what is intended.

Before you push, run what the test check runs. Both blocking build checks call
a workflow held in another repository, so the steps are read from there rather
than restated here:

```
gh api repos/jellyfin/jellyfin-meta-plugins/contents/.github/workflows/test.yaml \
  --jq .content | base64 -d
```

Today that prints a restore, a `dotnet build --configuration Release
--no-restore` and a `dotnet test --no-restore --verbosity normal`.

The build check is not the same three commands. It hands the tree to the
Jellyfin plugin repository manager and uploads the package that comes out, so
what it establishes is that the plugin packages, and a plain `dotnet build`
passing locally does not stand in for it.

## What a good issue contains

An issue says what is wrong, what the evidence is, and what done means.

Where the evidence is a number or a state of the tree, it carries the command
that produced it, run against the reference a reader will have rather than
against your working copy. A number without its command is a claim, and it is
fine to write a claim as long as it is written as one.

The done condition is a thing somebody else can check without asking you what
you meant. "The command above returns nothing" is a done condition. "Tidy this
up" is not.

One topic per issue, and one topic per pull request. Where a change turns out
to be two topics, the repair is to split the issue rather than to split the
finished diff.

## What a pull request body contains

The template in `.github/pull_request_template.md` is the shape. Everything
about the change goes in the body, including a reason a change was sent back. If
the body is wrong or out of date, edit the body.

Where the change adds or moves a guard, the body shows the guard biting: the run
that passes, the same run with the thing the guard names broken, and the revert.
A guard that could not have failed proves nothing.

Where something was not run, the body says so. An admission that a check was
skipped stays an admission through every later edit of that body.

## Style

English in tracked files. No generated-by markers, tool names or attribution to
anything other than a person in anything tracked.

Commit messages state what changed and what failure it prevents. Where the
commit is a correction, the message says what was wrong and how it was found.
