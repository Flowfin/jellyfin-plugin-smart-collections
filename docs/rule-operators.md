# The operators a rule may name

The operator set is closed. Which operators exist, which types of field each one
applies to, which types of value each one takes, how many values it is written
with and what each one means are declared in `RuleOperatorTable`, and this page
is that table written out.

Each section below carries a marker line of the form `## Operator: <name>`, a
`Field types:` line, a `Value types:` line, a `Values written:` line and a
`Semantics:` line. The names are what a rule document writes, the lists, the
counts and the sentences are what the table declares, and
`RuleOperatorDocumentTests` holds the page to the table in every one of those
directions, so an operator added without a section reds the suite and a section
describing an operator that does not exist reds it too.

## Why the set is closed

Both existing Jellyfin plugins in this space derive part of their operator set
from a .NET enum at runtime: whatever `System.Linq.Expressions.ExpressionType`
parses is an operator. Three things follow. The legal set is a framework detail
and moves when the framework does. It cannot be documented, because nobody wrote
it down and nothing could. And it cannot be held stable across versions, because
nobody chose it in the first place.

A declared set can be documented, can be validated against, and changes only
when somebody changes it in a diff a reader sees.

## What is deliberately absent

`matchRegex` is not here. `rule-language.md` carries that refusal and its
reason, and names the replacements: `contains`, `startsWith`, `endsWith`,
`equals` and `in`.

Ordering over text is not here either. `greaterThan` and its three neighbours
accept no string, because ordering text is either culture-sensitive, which would
make one rule collect different items on two servers, or ordinal, which orders
by code point and is almost never what somebody writing a rule means.

## What the `Values written:` line means

A type says what a value may be. It does not say how many of them the condition
writes, and the two are separate questions: `equals` and `in` accept the same
seven types, and one of them is written

```
{ "field": "genres", "operator": "equals", "value": "Thriller" }
```

while the other is written

```
{ "field": "officialRating", "operator": "in", "value": ["PG", "PG-13"] }
```

Three answers. `none` is `isEmpty` and `isNotEmpty`, which ask about the field
alone and carry no `value` member at all; a condition applying one with a value
beside it is refused rather than having the value dropped. `a list` is `in` and
`notIn`, written as a JSON array with at least one member; an empty array is
refused, because a condition comparing a field against no value narrows nothing
and whoever wrote it meant to fill it in. `one` is the other thirteen.

The count is declared rather than inferred from what a document happens to
write. A stage that read one value beside `in` and accepted it would mean
something the operator's own sentence does not say, and it would do so silently.

## What the two type lines mean

A condition has two ends: the field it names and the value written beside it. A
`Field types:` line is the set of field types the operator applies to, and a
`Value types:` line is the set of types the value beside it may be written as.
Both are properties of the operator and not of any field: the field's own row
declares the type it holds, and the two are compared where a condition is
validated.

For sixteen of the seventeen operators the two lines are the same list, because
the field and the value beside it hold the same type. `withinLast` is the one
where they differ. It applies to a field holding an instant and takes a length
of time beside it, which is what `dateAdded withinLast P30D` says and what its
own sentence describes.

THIS PAGE CARRIED ONE LINE UNTIL 2026-08-30 AND THE COLUMN BEHIND IT CARRIED
BOTH MEANINGS. Read as the field's type, `withinLast` said it applied to a field
holding a length of time, and asking whether a length of time is inside a span
ending now is not a question anybody writing a rule means. Read as the value's
type it said what it says now. Because the cross-table check in the suite reads
the field end, no date field could declare the operator and no duration field
would want it, so `withinLast` was in the closed set and reachable from no rule
anyone could write.

Two operators take no value at all. `isEmpty` and `isNotEmpty` ask about the
field alone, so their `Value types:` line is `none` and a condition writing a
value beside one of them is refused rather than having the value ignored. Their
`Field types:` line is every declared type, because the question they ask is one
every type answers - that line is not `none` and the two lines saying different
things is exactly what this page separates them for.

## Operator: equals

Field types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Value types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Values written: one

Semantics: The field is exactly the value.

## Operator: notEquals

Field types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Value types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Values written: one

Semantics: The field is anything other than the value.

## Operator: contains

Field types: String

Value types: String

Values written: one

Semantics: The field holds the value somewhere inside it.

## Operator: notContains

Field types: String

Value types: String

Values written: one

Semantics: The field holds the value nowhere inside it.

## Operator: startsWith

Field types: String

Value types: String

Values written: one

Semantics: The field begins with the value.

## Operator: endsWith

Field types: String

Value types: String

Values written: one

Semantics: The field ends with the value.

## Operator: in

Field types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Value types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Values written: a list

Semantics: The field is one of the values in the list.

## Operator: notIn

Field types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Value types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Values written: a list

Semantics: The field is none of the values in the list.

## Operator: greaterThan

Field types: Integer, Decimal, Date, Duration

Value types: Integer, Decimal, Date, Duration

Values written: one

Semantics: The field is above the value.

## Operator: greaterThanOrEqual

Field types: Integer, Decimal, Date, Duration

Value types: Integer, Decimal, Date, Duration

Values written: one

Semantics: The field is the value or above it.

## Operator: lessThan

Field types: Integer, Decimal, Date, Duration

Value types: Integer, Decimal, Date, Duration

Values written: one

Semantics: The field is below the value.

## Operator: lessThanOrEqual

Field types: Integer, Decimal, Date, Duration

Value types: Integer, Decimal, Date, Duration

Values written: one

Semantics: The field is the value or below it.

## Operator: isEmpty

Field types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Value types: none

Values written: none

Semantics: The field holds nothing.

## Operator: isNotEmpty

Field types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Value types: none

Values written: none

Semantics: The field holds something.

## Operator: before

Field types: Date

Value types: Date

Values written: one

Semantics: The field is earlier than the value.

## Operator: after

Field types: Date

Value types: Date

Values written: one

Semantics: The field is later than the value.

## Operator: withinLast

Field types: Date

Value types: Duration

Values written: one

Semantics: The field is inside the span that ends at the instant the evaluation was given.

## What a refusal names, now that a condition can be read

Which operators a given field accepts is the field table's, and it is in this
tree: `rule-fields.md` is that table written out, and each row there carries the
operators that mean something for that field.

THIS SECTION SAID A DOCUMENT NAMING AN UNKNOWN OPERATOR WAS REFUSED WITH ALL
SEVENTEEN NAMES, AND SAID BEFORE THAT THERE WAS NO FIELD TABLE AT ALL. The table
landed in `#203` and the stage that reads a condition's operator against it has
landed since, so both halves of what stood here are spent. What a condition meets
now is the narrow list:

    There is no operator called "matchRegex". The operators for a "genres" field are contains, notContains, isEmpty, isNotEmpty.

That is narrower than this section used to promise. It lists the operators the
FIELD'S ROW declares rather than the ones its type allows, because a list that
named operators the field refuses would send somebody repairing a document to a
second refusal.

The wide list is still what a caller with no field to narrow against builds, and
this page keeps describing it for that reason rather than because nothing uses
the other.

## Two refusals sit between a declared operator and an accepted one

They are different statements and they are deliberately not one message.

    The operator "contains" does not apply to a field of type Integer. It applies to a field of type String.

is the operator set's answer: the comparison is not defined over a whole number
at all, whatever field it was written against.

    The field "genres" does not accept the operator "startsWith". It accepts contains, notContains, isEmpty, isNotEmpty.

is the field row's answer: the comparison IS defined over strings, and this
particular field does not offer it, because a prefix test over a list of genres
means nothing. One message for both would tell whoever wrote the second document
that the operator does not work on text, which is false and sends them the wrong
way.
