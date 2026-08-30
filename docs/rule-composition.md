# How a rule composes its conditions

A rule with one condition is not interesting. This page is how conditions are
combined, how deep that may go, and what is refused.

Each group below carries a marker line of the form `## Group: <name>`, and the
names are what `RuleCompositionReader` accepts. `RuleCompositionDocumentTests`
holds the page to the reader in both directions, so a group added without a
section reds the suite and a section describing a group that does not exist reds
it too.

## Three groups and no separate negation

## Group: allOf

Everything the group holds matches.

## Group: anyOf

At least one of the things the group holds matches.

## Group: noneOf

Nothing the group holds matches.

Between them they cover conjunction, disjunction and negation. There is
deliberately no `not` on an individual condition: a document that can negate
anywhere hides the negation among the conditions, and somebody checking what a
rule collects then has to hold every condition in their head at once. With the
negation on the group it is in one place a reader can see.

Each group holds an array of members, and each member is either a condition or a
further group. Both are JSON objects; a group is an object carrying exactly one
of `allOf`, `anyOf` and `noneOf`, and anything else is a condition.

```
{
  "allOf": [
    { "field": "...", "operator": "...", "value": "..." },
    { "anyOf": [{ "field": "..." }, { "field": "..." }] }
  ]
}
```

## Nesting is bounded, and the bound is 4

The outermost group counts as one, so four levels allows a group of groups of
groups. Beyond that a rule is one nobody can check by reading, which defeats the
point of a rule being declared rather than programmed.

An unbounded tree is also a stack whose size the document decides, and a rule
document is untrusted text. That half is bounded twice over: the JSON parser
refuses a document nested past its own default depth before this stage sees it,
so the bound here is the readable one rather than the safety one.

The number is a named constant, `RuleCompositionReader.MaximumNestingDepth`, and
a test holds this page to it. There is no setting that raises it on a running
server, because a limit an operator can turn off is a limit that is off on the
server where it mattered.

## What is refused

An **empty group** is refused. Reading it as matching everything and reading it
as matching nothing are both defensible, which is exactly why neither may be
chosen quietly: an operator who deleted the last condition out of a group gets a
message rather than a collection that silently swallowed their library or
emptied itself.

**Two group members in one object** is refused rather than resolved by an order
this plugin would have to invent. An object carrying two of them is two rules
written on top of each other, and whichever one the writer meant, the other one
is silently doing something.

A **member that is not an object** is refused, because both things a group may
hold are objects.

A **group whose member is not an array** is refused.

Every reason is reported rather than the first. A composition is where an
operator's typing mistakes collect, and repairing them one run at a time is the
slowest way to fix a file.

## What this stage does not read

A condition. What a condition may say is the field vocabulary's business and
arrives as its own stage over the same text. This stage carries each condition
as the place it sits in the document, so a document with a malformed condition
can still be told from one whose groups are wrong, which are different repairs.

The tree preserves the order the document wrote. That is not the same as the
compiled form being independent of that order, which is a property of the
compiler rather than of this stage, and it is owed where the compiler is.
