# The operators a rule may name

The operator set is closed. Which operators exist, which value types each one
accepts and what each one means are declared in `RuleOperatorTable`, and this
page is that table written out.

Each section below carries a marker line of the form `## Operator: <name>`, a
`Value types:` line and a `Semantics:` line. The names are what a rule document
writes, the type lists and the sentences are what the table declares, and
`RuleOperatorDocumentTests` holds the page to the table in both directions, so
an operator added without a section reds the suite and a section describing an
operator that does not exist reds it too.

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

## What a value type list means

An operator's list is the set of types it can compare a value of. It is a
property of the operator and not of any field: the field's own row declares the
type it holds, and the two are compared where a condition is validated.

Two operators list no type at all. `isEmpty` and `isNotEmpty` ask about the
field alone, so a condition writing a value beside one of them is refused rather
than having the value ignored.

## Operator: equals

Value types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Semantics: The field is exactly the value.

## Operator: notEquals

Value types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Semantics: The field is anything other than the value.

## Operator: contains

Value types: String

Semantics: The field holds the value somewhere inside it.

## Operator: notContains

Value types: String

Semantics: The field holds the value nowhere inside it.

## Operator: startsWith

Value types: String

Semantics: The field begins with the value.

## Operator: endsWith

Value types: String

Semantics: The field ends with the value.

## Operator: in

Value types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Semantics: The field is one of the values in the list.

## Operator: notIn

Value types: String, Integer, Decimal, Boolean, Date, Duration, Enumeration

Semantics: The field is none of the values in the list.

## Operator: greaterThan

Value types: Integer, Decimal, Date, Duration

Semantics: The field is above the value.

## Operator: greaterThanOrEqual

Value types: Integer, Decimal, Date, Duration

Semantics: The field is the value or above it.

## Operator: lessThan

Value types: Integer, Decimal, Date, Duration

Semantics: The field is below the value.

## Operator: lessThanOrEqual

Value types: Integer, Decimal, Date, Duration

Semantics: The field is the value or below it.

## Operator: isEmpty

Value types: none

Semantics: The field holds nothing.

## Operator: isNotEmpty

Value types: none

Semantics: The field holds something.

## Operator: before

Value types: Date

Semantics: The field is earlier than the value.

## Operator: after

Value types: Date

Semantics: The field is later than the value.

## Operator: withinLast

Value types: Duration

Semantics: The field is inside the span that ends at the instant the evaluation was given.

## What this page cannot yet say

Which operators a given field accepts is the field table's, and there is no
field table in this tree. Until there is, a document naming an operator no
operator has is refused with every legal name rather than with the ones that
suit the field it was written against, and that narrowing is one call site on
the day the field table lands.
