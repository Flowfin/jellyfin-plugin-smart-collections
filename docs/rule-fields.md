# The fields a rule may name, and the item kinds it may collect

The field vocabulary is closed. Which fields exist, what type each one holds and
which operators each one accepts are declared in `RuleFieldTable`, and this page
is that table written out. Which of those operators the server's own query
answers is declared one pair at a time in `RuleQueryTable`, and the
`Answered by the query:` line on each section below is that table read for one
field.

Each field section below carries a marker line of the form `## Field: <name>`, a
`Value type:` line, an `Operators:` line, a `Kinds:` line, an
`Answered by the query:` line and a `Semantics:` line. The names are what a rule
document writes, and `RuleFieldDocumentTests` holds the page to the table in both
directions, so a field added without a section reds the suite and a section
describing a field that does not exist reds it too.

The item kinds are the second closed list on this page, declared in
`RuleItemKindTable` and written out under `## Item kind: <name>` in the same
way. They are here rather than on a page of their own because a rule's scope and
a rule's fields are read together: the scope decides what the query asks for and
the fields decide what is asked of it, and splitting them puts half of one
sentence on each of two pages.

## What a `Kinds` line means

The item kinds the field means anything for. A condition on a field that means
nothing for any kind the rule collects is refused when the document is read,
with a message naming the field, what the rule collects and what the field
applies to, so whoever wrote it can see which of the two is wrong.

Every field on this page names both kinds, so no document you can write today
reaches that refusal. The column is declared rather than inferred anyway: a
field added later that means nothing for one kind would otherwise be a silent
acceptance instead of a line somebody had to write, and a rule collecting films
and series would ask the server a question about half of what it collects with
nothing saying so.

**The refusal is per rule and not per kind.** A rule over films and series with
a condition on a field that means something for a film and nothing for a series
is accepted: it narrows the films and leaves the series alone, which is what the
document says. What is refused is a condition that means nothing for anything
the rule collects at all.

## Why the vocabulary is declared

Both existing Jellyfin plugins in this space resolve a rule's field by looking
its string up as a property on a projection class, with
`Expression.PropertyOrField`. Three things follow. The legal set is whatever
properties happen to sit on that class, so nobody chose it. It is written down
nowhere a person writing a rule can read, so a name has to be guessed. And a
guess that is wrong arrives at evaluation as an exception rather than at
validation as a message naming the legal ones.

A declared table can be listed back to whoever mistyped a name, can be
validated against before anything runs, and changes only when somebody changes
it in a diff a reader sees.

## What an `Answered by the query` line means

The operators, out of the ones on the `Operators:` line above it, that the
server's own item query answers for that field. Every other operator on that line
is answered by the stage that runs over what the query returned. `none` means the
query answers no way of asking about the field at all, so every condition on it
is answered by that stage.

THIS LINE USED TO BE ONE QUERY PROPERTY OR THE WORDS `after the query`, AND THAT
SHAPE COULD NOT BE RIGHT. It came off a column on the field row that named the
one property the field narrowed on, and a field is answered by the server under
some of its operators and not under others: `communityRating` accepts four
comparisons and the query answers one of them, so the old line said "narrowed by
the query" about three conditions the stage answers. #31 decided on 2026-09-04
that the mark is a property of the field and operator PAIR, the column is gone,
and this line is derived from the pair table rather than from a second
declaration that had to be kept in step with it.

The properties each pair writes are held to the server this leg is compiled
against by `RuleFieldQuerySurfaceTests`, which reflects over the
`InternalItemsQuery` the suite has rather than reading a list somebody typed.
That is the same trap `#11` is about, one surface along: a property that exists
on the newer line and not on the older one would compile here and throw on a
10.11 server.

WHAT EACH PAIR WRITES IS NOT THIS LINE. This one says whether the query answers a
pair; [`rule-queries.md`](rule-queries.md) says what each answered pair puts on
the query and what that means, pair by pair.

## What an `Operators` line means

The operators that mean something for that field, which is a subset of what the
operator set says a field of that type allows and never a superset. The operator
table answers whether an operator applies to a field of a type at all; a row
here answers whether the comparison means anything for this particular field,
which is the narrower question and the one somebody writing a rule is asking.

THE `Value type:` LINE ABOVE IS THE FIELD'S OWN TYPE AND NOT ALWAYS THE TYPE OF
THE VALUE BESIDE A CONDITION. For sixteen of the seventeen operators the two are
the same type. `withinLast` is the one where they are not: `dateAdded` holds a
`Date` and `dateAdded withinLast P30D` writes a `Duration` beside it.
`rule-operators.md` carries a `Field types:` line and a `Value types:` line per
operator, which is where that pair is read.

NO FIELD DECLARED `withinLast` UNTIL 2026-08-30, AND THIS PAGE RECORDED THAT AS
A DEFECT IN THE OPERATOR SET. It was one: the operator set declared a single
type column, read as the type the FIELD declares, and `withinLast` put `Duration`
in it, so no date field could declare the operator without the cross-table check
in the suite refusing the row and no duration field would have wanted it. The
repair landed in the operator set rather than here, the column is two columns,
and the two date fields below declare the operator.

A CONDITION IS REFUSED AGAINST THIS LINE, WHICH IT WAS NOT UNTIL THE STAGE THAT
READS AN OPERATOR LANDED. A document writing an operator this row does not
declare is refused with the row's own list, and a document writing one no
operator has is refused with the same list rather than with all seventeen.
`rule-operators.md` carries both sentences and the reason they are two.

`rule-operators.md` is where each operator's own sentence lives, and
`rule-values.md` is where the written form of each value type lives. Neither is
restated here.

## What is deliberately absent

No field describes one person's viewing. `rule-language.md` carries that refusal
and its reason.

No field holds an enumeration. A field of that type owes a column naming the
values it accepts, because the enumeration parser is handed that list, and no
field in this first vocabulary has one. The column arrives with the first field
that needs it rather than being carried empty by ten rows that do not.

Which item kinds each FIELD applies to IS here, on the `Kinds:` line of every
section, and it is not the same question as the list below. That list says what a
RULE may collect; the `Kinds:` line says which kinds a field means anything for,
which is the narrower answer. Every field on this page names both kinds today, so
the column narrows nothing yet and refuses nothing yet.

## The item kinds a rule may collect

Every rule document carries a `collects` member naming one or more of the kinds
below. It is required. The two ways of leaving it out are both worse than
refusing it: defaulting to every kind makes every rule a walk over the whole
library, and inferring the scope from the fields a rule happens to name makes
adding one condition silently change the size of the query. Both read well on a
small library and neither can be explained to somebody whose server got slower.

The order the names are written in means nothing, because a scope is a set. Two
documents naming one set in two orders compile to the same query, and a name
written twice is refused rather than folded away: a repeat changes nothing about
what the rule collects, so it is most often a half-finished edit and is left to
be repaired.

The accepted list is this plugin's own rather than the server's enumeration. A
legal set derived from a framework enumeration moves when the framework does,
cannot be listed back to whoever mistyped a name, and cannot be documented. That
is the same argument the field vocabulary rests on, one surface along.

A `Selects:` line names the member of the server's own item kind enumeration a
kind reaches the library through. No member of that enumeration carries an
explicit value, so what a compiled query asks the server for is the member's
POSITION in that declaration rather than its name. The two supported lines agree
on every position today, and `RuleItemKindServerSurfaceTests` holds the whole
enumeration to a checked-in ordered list rather than to a set, so a line that
later inserts a member anywhere but at the end reds the suite instead of shipping
a package that asks for the wrong kind.

Which kinds the first version accepts was decided on 2026-08-24, as question 10
of `#67`. Widening the list later is one row, one section here and one line of
that expected list.

## Item kind: movie

Selects: BaseItemKind.Movie

Semantics: A film.

## Item kind: series

Selects: BaseItemKind.Series

Semantics: A series, which is the show rather than any of its seasons or episodes.

## The fields read after the query

THE FIELDS THE QUERY ANSWERS NOTHING ABOUT, which is a narrower set than the
conditions the post-query stage answers and is worth keeping apart from it. Since
#31 the mark is a property of the field and operator pair, so most fields on this
page have some conditions the query answers and some the stage does; the fields
below have none of the first kind, and a field arriving here is the change that
takes a whole part of the vocabulary out of the query.

They are listed with the reason each one is here rather than only being derivable
from the absence of rows, because that is a decision somebody should have to see:
the stage is where a design like this loses its speed if it is allowed to grow.

The list is derived from the tables rather than typed beside them.
`RuleFieldDocumentTests` requires the set below to be exactly the fields no
compiled pair answers, in both directions, so a field whose last pair is taken out
of the query without a section here reds the suite and a section for a field the
query answers something about reds it too.

WHICH CONDITIONS THE STAGE ANSWERS IS THE WIDER SET AND IS NOT LISTED ANYWHERE.
It is every pair the `Operators:` lines above allow that the
`Answered by the query:` lines do not name, which is most of them. A list of that
here would be a copy of two tables that drifts against them; what each field
section carries instead is the half a reader is asking about.

A `Reason:` line says why the query cannot carry the field. It is a fact about the
server rather than about this plugin, and each one below carries the command that
was run to read it.

## Read after the query: overview

Reason: The server's item query carries one property about a description and it asks only whether the item has one, so no comparison over the text can be pushed into it.

The property is `HasOverview`, a nullable boolean, on both supported lines:

```
for ref in v10.11.11 v12.0-rc4; do
  gh api "repos/jellyfin/jellyfin/contents/MediaBrowser.Controller/Entities/InternalItemsQuery.cs?ref=$ref"     --jq .content | base64 -d | grep -nE 'Overview'
done
152:        public bool? HasOverview { get; set; }
130:            || HasOverview.HasValue
265:        public bool? HasOverview { get; set; }
```

WHAT THAT LEAVES OPEN is `isEmpty` and `isNotEmpty`, which ask exactly the
question that property answers rather than a question about the text. WHAT THE
SERVER MEANS BY `HasOverview` FOR AN ITEM WHOSE DESCRIPTION IS PRESENT AND BLANK
IS MEASURED NOW, AND THIS PARAGRAPH SAID NOTHING HERE HAD MEASURED IT. Both
supported lines translate the property the same way, and a blank description
answers as no description on both. The two lines hold the translation in
different files, so the path is read per line rather than the reference alone:

```
for spec in "v10.11.11 BaseItemRepository.cs" \
            "v12.0-rc4 BaseItemRepository.TranslateQuery.cs"; do
  set -- $spec
  echo "== $1"
  gh api "repos/jellyfin/jellyfin/contents/Jellyfin.Server.Implementations/Item/$2?ref=$1" \
    --jq .content | base64 -d | grep -A11 'filter.HasOverview.HasValue' \
    | grep -oE '\.Where\(e => .*\);'
done
== v10.11.11
.Where(e => e.Overview != null && e.Overview != string.Empty);
.Where(e => e.Overview == null || e.Overview == string.Empty);
== v12.0-rc4
.Where(e => e.Overview != null && e.Overview != string.Empty);
.Where(e => e.Overview == null || e.Overview == string.Empty);
```

So the property does not separate an absent description from a blank one, in
either direction, and the question this paragraph left for a later reader has an
answer rather than a gap.

WHAT STOOD IN FRONT OF THOSE TWO PAIRS IS GONE, AND THEY ARE STILL NOT DECLARED.
This section said the mark that puts a field after the query is a column on the
FIELD rather than on the pair, so it could not say that two of a field's six
operators compile and the other four do not, and that whether the mark should
move was a decision nobody had taken. It was taken on 2026-09-04 and executed:
the column is gone and the mark is `RuleQueryTable.AnswersInTheQuery`, which
takes both halves of the pair.

So `overview isEmpty` and `overview isNotEmpty` are now expressible as answered
pairs, with the server's translation read above and this page carrying it. They
are not added here, because adding a pair to the compile table changes what a
document DOES rather than where a mark lives, and the two do not belong in one
change. `overview` therefore still reads `none`, and this paragraph is what a
later reader needs so the opportunity is not found from scratch a third time.

## Read after the query: runtime

Reason: The server's item query carries no property over how long an item runs, so every comparison on it is made over the items the query returned.

```
for ref in v10.11.11 v12.0-rc4; do
  gh api "repos/jellyfin/jellyfin/contents/MediaBrowser.Controller/Entities/InternalItemsQuery.cs?ref=$ref"     --jq .content | base64 -d | grep -cE 'Runtime|RunTimeTicks'
done
0
0
```

## Field: communityRating

Value type: Decimal

Operators: greaterThan, greaterThanOrEqual, lessThan, lessThanOrEqual

Kinds: movie, series

Answered by the query: greaterThanOrEqual

Semantics: The rating the community gives the item, out of ten.

## Field: dateAdded

Value type: Date

Operators: before, after, withinLast

Kinds: movie, series

Answered by the query: after

Semantics: When the server first saw the item.

## Field: genres

Value type: String

Operators: contains, notContains, isEmpty, isNotEmpty

Kinds: movie, series

Answered by the query: contains

Semantics: The genres the item carries.

## Field: name

Value type: String

Operators: equals, notEquals, contains, notContains, startsWith, endsWith, in, notIn

Kinds: movie, series

Answered by the query: equals

Semantics: The title the library holds for the item.

## Field: officialRating

Value type: String

Operators: equals, notEquals, in, notIn, isEmpty, isNotEmpty

Kinds: movie, series

Answered by the query: equals, in

Semantics: The age classification the item carries.

## Field: overview

Value type: String

Operators: contains, notContains, startsWith, endsWith, isEmpty, isNotEmpty

Kinds: movie, series

Answered by the query: none

Semantics: The description the library holds for the item.

## Field: premiereDate

Value type: Date

Operators: before, after, withinLast

Kinds: movie, series

Answered by the query: before, after, withinLast

Semantics: When the item was first released.

## Field: productionYear

Value type: Integer

Operators: equals, notEquals, in, notIn, greaterThan, greaterThanOrEqual, lessThan, lessThanOrEqual

Kinds: movie, series

Answered by the query: equals, in

Semantics: The year the item was produced.

## Field: runtime

Value type: Duration

Operators: greaterThan, greaterThanOrEqual, lessThan, lessThanOrEqual

Kinds: movie, series

Answered by the query: none

Semantics: How long the item runs for.

## Field: tags

Value type: String

Operators: contains, notContains, isEmpty, isNotEmpty

Kinds: movie, series

Answered by the query: contains, notContains

Semantics: The tags the item carries.
