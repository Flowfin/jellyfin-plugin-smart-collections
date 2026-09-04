# The rule corpus

One rule document per `.json` file, and beside each one a `.expected.txt` holding
the answer it got. `RuleCorpusTests` compares the two on every run of the suite,
so a change that moves what a document compiles to has to move an expected file
in the same commit, which is the moment for a reader to ask whether the move was
intended.

## What an expected file holds

The compiled query, and not the ordered identifier list of the items a rule
collects. That was decided on #45 on 2026-09-04, and the two are different
guards rather than a strong and a weak version of one:

- **This file** catches a change that moves what a document compiles to. It is
  blind to everything after the query.
- **The identifier list** is a second expected file over these same documents,
  owned by the issue that runs a compiled query, and it cannot exist until
  something does.

So a green run here says the documents compile to the queries they compiled to
before. It says nothing about which items a server would return for one.

## The shape of an expected file

A leading run of `#` lines saying what the document is for, then one line per
fact, in this order:

```
scope: movie, series
query: <property>=<value>
after the query: <pointer> <field> <operator> [<values>]
```

A document the read refuses renders `refused: <pointer>: <message>` and nothing
else. One the read accepts and the compiler refuses renders `refused by the
compiler: ...`, under its own prefix so a reader can tell which of the two
stages produced the answer.

The `query:` lines are the properties a freshly constructed query does NOT
carry, by name. Only those, because the two supported server lines declare
different numbers of properties and a rendering of all of them would need one
expected file per line. What that cannot see is what `QuerySnapshot` records
about itself: a property whose value has no value equality renders as its type
name, and a property with no getter is not read at all.

## The instant

Every document here is compiled at `RuleCorpus.EvaluatedAt`, which is fixed.
A rule saying "in the last thirty days" compiles against the instant the
evaluation was given, so a corpus that read the machine's clock would rewrite
its own expected files every day and prove nothing.

## Regenerating

`docs/testing.md` carries the command. A regeneration always leaves the suite
red, by one test that says so, so it cannot be mistaken for a run that found
nothing.

## The vocabulary here is a fixture vocabulary

The genres, tags, names and ratings these documents write exist to reach a pair
in the query table. They describe nobody's library, and no test here asserts
anything about this repository's own state.
