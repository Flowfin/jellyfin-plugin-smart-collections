# What a rule deliberately cannot say

A rule language is defined as much by its refusals as by its vocabulary. This
file records the refusals, each with its reason, so that a later request to add
one is argued against a written position rather than against nothing.

Nothing here is permanent. A refusal can be lifted, and the way to lift one is
to argue with the reason recorded under it. What is not available is lifting one
quietly.

Each refusal below carries a marker line of the form `Refusal: <name>`. A test
holds this file to that list, so a refusal cannot leave the document by
accident.

Three of the refusals below were put as a question before they were written
down, and each of the three carries a line naming its question on #67 and the
day it was answered. The other four rest on the reason under them and on nothing
else. A test holds that set in both directions, so a refusal cannot lose its
line quietly and a refusal that was never a question cannot gain one.

Until 2026-08-24 those three lines said the question behind them had no answer
recorded. All ten questions on #67 were answered on that day, none of these
three against its working assumption, so what changed is what the reader is told
about where the refusal came from rather than the refusal.

## Refusal: regular expressions

This refusal is the answer recorded on question 6 of #67, decided 2026-08-24.

A rule document is untrusted text evaluated on a server task thread, and a
pattern with catastrophic backtracking there stops the server doing anything
else until it finishes, which may be never.

This is not a theoretical property of the class. The longest-lived plugin in
this space accepts operator-supplied patterns and compiles them, and it bounds a
single match at one second with no non-backtracking engine. Read at a commit
rather than at a branch, so the paste below goes on reproducing:

```
ref=282d5701c88bfcdcc170e3ce7bef0357bc72add1
gh api "repos/jyourstone/jellyfin-smartlists-plugin/contents/Jellyfin.Plugin.SmartLists/Core/QueryEngine/Engine.cs?ref=$ref" \
  --jq .content | base64 -d | grep -nE 'RegexMatchTimeout = |new Regex|NonBacktracking'
51:        internal static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(1000);
108:                    return new Regex(key, RegexOptions.Compiled | RegexOptions.None, RegexMatchTimeout);
```

What that bound covers is one match and not one refresh, and the plugin says so
in its own remark beside the handler: item processing is serialised there, so a
pattern that times out once would cost the timeout again on every item, and the
refresh is failed rather than continued. So the hazard is taken seriously enough
to be bounded by the plugin that accepts patterns, and the bound stops one match
rather than the walk over a library.

That paste read `?ref=main` until 2026-08-22 and quoted a construction with no
timeout argument at line 62, and a count of zero for `MatchTimeout` and
`NonBacktracking`. `main` moved and the reading stopped reproducing, which is
why the reference above is a commit. Nothing in this repository re-runs either
command, so a reader who wants the plugin as it stands today runs it against
`main` and compares.

That plugin is AGPL-3.0 and this one is GPL-3.0, so no code moves between them
in either direction. What is being read is a behaviour and the shape of a
mistake, not source.

The declared replacements are `contains`, `startsWith`, `endsWith`, `equals` and
`in`, each with an explicit case sensitivity flag. Between them they cover the
patterns operators actually write, without handing the server a program to run.

If regular expressions are ever added, they arrive with three things together: a
non-backtracking engine, a match timeout, and a test proving a known
catastrophic pattern is refused rather than run. Two of the three is not the
condition, and the plugin read above has one of them.

## Refusal: arbitrary expressions

No field takes code, no operator evaluates a string as an expression, and
nothing in the engine compiles a document into a delegate.

The compiled form of a rule is a library query plus a small declared set of
post-query predicates. Both are inspectable, which is what makes it possible to
say what a rule will do before running it. An expression compiled from a
document is inspectable only by running it.

## Refusal: cross-item aggregates

A condition such as "every film by a director who has more than five films in
this library" needs a second pass over the library for each candidate item, and
turns one refresh into a quadratic walk.

Grouping and counting are the natural next request, and they belong in a version
that has measured that cost rather than in the one that discovers it on somebody
else's library.

## Refusal: references between collections

A rule saying "everything in collection A that is not in collection B" makes the
order collections refresh in significant, and lets two collections oscillate
between runs.

Collections are outputs. A rule reads the library, and the library is not
changed by another rule's output.

## Refusal: the wall clock as an implicit input

Relative dates are allowed. Reading the clock implicitly is not.

An evaluation that calls the current time in the middle of matching has an
answer that cannot be reproduced, because the input it depended on is gone by
the time anyone asks. The determinism milestone requires the instant to be
injected and recorded, so a rule using a relative date has one answer that can
be produced again from the record.

## Refusal: fields describing one person's viewing

This refusal is the answer recorded on question 1 of #67, decided 2026-08-24.

A Jellyfin collection is server-wide, and every account sees the same one. A
rule about what one person has watched, favourited or rated would build a list
everyone sees out of one person's state, which is a surprise for every account
that did not write the rule.

Evaluating such a field against one named account is deterministic and would
work. It is refused for the first version because it puts a confusing object in
front of every user of the server, and because a later version with a real
per-user story can still add it.

## Refusal: pinning an item into a collection

This refusal is the answer recorded on question 2 of #67, decided 2026-08-24.

Membership comes from the rule, so an item added to a generated collection by
hand is removed on the next refresh.

This will surprise people, and it keeps the behaviour to one sentence. The
alternatives, a list of pinned identifiers in the document or a pin expressed as
a tag on the item, each add a second source of membership, and the question
"why is this item here" stops having one answer.

## What is not in this file

Which item kinds a rule may collect, and which fields the vocabulary holds, are
not refusals. They are the declared vocabulary, and they live in the field table
and its own reference rather than here. A field that is absent from the table is
absent, not refused, and adding one is a row and a test.
