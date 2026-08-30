# The value types a rule carries, and the form each one is written in

A rule compares a field against a value. Which type that value holds is declared
by the field, not guessed from what the value looks like, and each type has one
parser and one written form. This page is that form, one section per type.

Each section below carries a marker line of the form `## Value type: <name>` and
an `Accepted form:` line. The names are the members of `RuleValueType` and the
sentences are what `RuleValueForm.Of` returns, both held to this page by
`RuleValueDocumentTests` in both directions, so a type added without a section
reds the suite and a section describing a type that does not exist reds it too.
The sentence a refusal shows an operator and the sentence on this page are the
same string rather than two wordings that agree today.

What is deliberately not here is which fields exist and which type each one
holds. That is the field table, and it lives with the fields.

## Parsing happens once

A value is parsed at validation and never again. Nothing downstream re-reads the
text, and nothing rewrites the document in memory: the parsed value is a
`RuleValue` whose members are get-only, with no setter of any kind, so a stage
after validation can read it and cannot change its mind about it.

That is worth stating because the prior art in this space does the opposite. It
converts a value with `Convert.ChangeType` against whatever type reflection
reported for a property, and rewrites dates into another representation by
mutating the parsed rule in place before evaluating it. Two things follow from
that shape: what a value will be converted into is invisible to the person
writing the rule, and running the conversion twice is not the same as running it
once.

## A value's spelling never decides its type

`"12"` is a string and `12` is a number, in the document and in the parser. A
value written in the wrong one of those is refused rather than converted, on
every field and in both directions.

The alternative is a document whose own types are advisory. Under it, a field
that changes its declared type silently changes the meaning of every value that
was written in the other spelling, and nobody editing the table can see which
documents that reaches.

## Nothing here reads the server's locale or its clock

Numbers are read out of the JSON parser rather than off a string. Dates and
durations are parsed against explicit formats with the invariant culture. Every
string comparison is ordinal. So the same document parses to the same values on
a server in any locale.

No parser reads a clock. A relative date is a duration written in the document
plus the instant the evaluation was given, and both of those are inputs, which
is what `rule-language.md` refuses the wall clock as an implicit input for.

## Value type: String

Accepted form: a JSON string

Nothing is trimmed and nothing is case-folded. A string is the text the document
wrote, and a plugin that quietly trimmed one would be answering a question the
operator did not ask.

```json
"Studio Ghibli"
```

## Value type: Integer

Accepted form: a JSON number with no fractional part, between -9223372036854775808 and 9223372036854775807

A number carrying a fractional part is refused rather than truncated, because a
truncation is a different rule from the one that was written and nothing would
say so.

```json
1997
```

## Value type: Decimal

Accepted form: a JSON number between -79228162514264337593543950335 and 79228162514264337593543950335

Decimal rather than a binary floating point type, so a rule comparing against a
rating an operator typed compares against the number they typed. `8.1` read into
the nearest `double` is not `8.1`, and a rule asking for equality against it
finds nothing.

A number written with more digits than a decimal holds is accepted and rounded
to the nearest one it does hold, rather than refused. That is the parser this
plugin inherits from the framework rather than a choice made here, and the bound
in the accepted form is the range, which is refused.

```json
8.1
```

## Value type: Boolean

Accepted form: the JSON literal true or the JSON literal false

The strings `"true"` and `"false"` are refused. They are values of the string
type, and a field declaring a boolean is asking for the literal.

```json
true
```

## Value type: Date

Accepted form: a JSON string holding an ISO 8601 date with an explicit offset, or an ISO 8601 date on its own

A date and time written with no offset is refused. Such a value names an instant
only once somebody supplies a zone, the only zone available at that point is the
server's, and a document that means a different instant on two servers is what
this plugin exists not to produce.

A date written on its own is read as the start of that day at offset zero. That
is a choice rather than a reading of anything, and it is the only one left once
the server's zone is refused. What it costs is that an operator on a positive
offset writing a day means an instant a few hours before their own midnight, and
the repair for anybody that matters to is to write the offset.

Two documents writing one instant with two offsets parse to one value. The
offset a value was written in is not carried, because an instant is the thing
being compared and the document keeps the text either way.

```json
"2026-08-30"
"2026-08-30T21:00:00+02:00"
"2026-08-30T19:00:00Z"
```

## Value type: Duration

Accepted form: a JSON string holding an ISO 8601 duration written in whole weeks, or in whole days, hours, minutes and seconds

Years and months are refused by name. How long either of them is depends on when
it is counted from, so a rule carrying one would mean a different span in
February than in March, and no expected output could hold it.

The designators appear in the order `W`, `D`, `T`, `H`, `M`, `S`, each at most
once, and at least one of them is present. Weeks do not combine with anything
else: `P1W2D` reads as nine days to one person and as a mistake to another, and
neither of them is wrong about the text. A `T` with nothing after it is refused,
because it is the second half of a duration somebody stopped writing.

```json
"P30D"
"P2W"
"PT12H30M"
```

## Value type: Enumeration

Accepted form: a JSON string holding one of the names the field declares

The names come from the field's own row rather than from a list held with the
type, because which names an enumeration accepts is a property of the field. A
value that is not one of them is refused with the list, so the operator reads
what they may write instead of guessing.

The comparison is ordinal. A name that differs only in case is a different name.

```json
"Movie"
```
