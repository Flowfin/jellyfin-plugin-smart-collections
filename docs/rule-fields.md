# The fields a rule may name, and the item kinds it may collect

The field vocabulary is closed. Which fields exist, what type each one holds,
which operators each one accepts and how each one reaches the library are
declared in `RuleFieldTable`, and this page is that table written out.

Each field section below carries a marker line of the form `## Field: <name>`, a
`Value type:` line, an `Operators:` line, a `Reaches the library:` line and a
`Semantics:` line. The names are what a rule document writes, and
`RuleFieldDocumentTests` holds the page to the table in both directions, so a
field added without a section reds the suite and a section describing a field
that does not exist reds it too.

The item kinds are the second closed list on this page, declared in
`RuleItemKindTable` and written out under `## Item kind: <name>` in the same
way. They are here rather than on a page of their own because a rule's scope and
a rule's fields are read together: the scope decides what the query asks for and
the fields decide what is asked of it, and splitting them puts half of one
sentence on each of two pages.

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

## What a `Reaches the library` line means

A field reaches the library one of two ways, and the line says which.

`InternalItemsQuery.<Property>` means the field is about that property of the
server's own query, so the query itself can narrow on it. The property name is
held to the 10.11 line by `RuleFieldQuerySurfaceTests`, which reflects over the
`InternalItemsQuery` the suite is compiled against rather than reading a list
somebody typed. That is the same trap `#11` is about, one surface along: a
property that exists on the newer line and not on the older one would compile
here and throw on a 10.11 server.

`After the query` means the server's query carries nothing to narrow on, so the
field is read off each item the query returned. Those fields are the reason the
post-query stage exists, and how small that stage may be is decided separately
from this page.

WHICH OPERATORS NARROW INSIDE THE QUERY IS NOT THIS LINE. A row names the
property a field is about; whether a particular comparison over that field can
be pushed into the query or has to be made afterwards is the compiler's
business, and this page makes no claim about it. `rule-queries.md` is where that
is declared, pair by pair.

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

Which item kinds each FIELD applies to is not here either, and it is not the same
question as the list below. That list says what a RULE may collect; a column
saying which kinds a field means anything for would be narrower, every field on
this page applies to both kinds the first version collects, so the absence states
nothing false, and the column that would carry a narrower answer is `#69`.

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

A field reaches the library through a property of the server's own item query or
it does not, and where it does not the condition is answered by reading each item
the query returned. Those fields are the whole of what the post-query stage
exists for, so they are listed here with the reason each one is on the list
rather than only marked one row at a time: a field arriving on it is a decision
somebody should have to see, because the stage is where a design like this loses
its speed if it is allowed to grow.

The list is derived from the table rather than typed beside it.
`RuleFieldDocumentTests` requires the set below to be exactly the rows carrying no
query property, in both directions, so a field that moves into the stage without a
section here reds the suite and a section for a field the query narrows on reds it
too.

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

WHAT THE ANSWER DOES NOT DO IS DECLARE THE PAIRS, and the reason has moved from a
reading nobody had taken to a shape this vocabulary holds. The mark that puts a
field after the query is a column on the FIELD rather than on the pair, so it
cannot say that two of a field's six operators compile and the other four do not:

```
git grep -n 'IsPostQuery =>' -- Jellyfin.Plugin.SmartCollections.Engine/Rules/RuleFieldRow.cs
Jellyfin.Plugin.SmartCollections.Engine/Rules/RuleFieldRow.cs:97:    public bool IsPostQuery => QueryProperty is null;
```

Both directions of that column are held, so neither half of the change passes on
its own. A pair compiled over a field the table marks post-query reds one test,
and a field moved off the mark to carry the pair leaves this page's list, which
reds another:

```
git grep -n 'public void NoFieldReadAfterTheQueryHasAPairThatCompiles\|public void TheListIsExactlyTheRowsCarryingNoQueryProperty' -- Jellyfin.Plugin.SmartCollections.Tests/
Jellyfin.Plugin.SmartCollections.Tests/RuleFieldDocumentTests.cs:194:    public void TheListIsExactlyTheRowsCarryingNoQueryProperty()
Jellyfin.Plugin.SmartCollections.Tests/RuleQueryTableTests.cs:68:    public void NoFieldReadAfterTheQueryHasAPairThatCompiles()
```

The pairs are therefore still not declared, and what stands in front of them is
whether the mark becomes a property of the pair rather than of the field. That
changes every row of both tables and is a decision about the vocabulary rather
than an entry in it, so it is not taken here. #31 is where the size of this stage
is argued.

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

Reaches the library: InternalItemsQuery.MinCommunityRating

Semantics: The rating the community gives the item, out of ten.

## Field: dateAdded

Value type: Date

Operators: before, after, withinLast

Reaches the library: InternalItemsQuery.MinDateCreated

Semantics: When the server first saw the item.

## Field: genres

Value type: String

Operators: contains, notContains, isEmpty, isNotEmpty

Reaches the library: InternalItemsQuery.Genres

Semantics: The genres the item carries.

## Field: name

Value type: String

Operators: equals, notEquals, contains, notContains, startsWith, endsWith, in, notIn

Reaches the library: InternalItemsQuery.Name

Semantics: The title the library holds for the item.

## Field: officialRating

Value type: String

Operators: equals, notEquals, in, notIn, isEmpty, isNotEmpty

Reaches the library: InternalItemsQuery.OfficialRatings

Semantics: The age classification the item carries.

## Field: overview

Value type: String

Operators: contains, notContains, startsWith, endsWith, isEmpty, isNotEmpty

Reaches the library: after the query

Semantics: The description the library holds for the item.

## Field: premiereDate

Value type: Date

Operators: before, after, withinLast

Reaches the library: InternalItemsQuery.MinPremiereDate

Semantics: When the item was first released.

## Field: productionYear

Value type: Integer

Operators: equals, notEquals, in, notIn, greaterThan, greaterThanOrEqual, lessThan, lessThanOrEqual

Reaches the library: InternalItemsQuery.Years

Semantics: The year the item was produced.

## Field: runtime

Value type: Duration

Operators: greaterThan, greaterThanOrEqual, lessThan, lessThanOrEqual

Reaches the library: after the query

Semantics: How long the item runs for.

## Field: tags

Value type: String

Operators: contains, notContains, isEmpty, isNotEmpty

Reaches the library: InternalItemsQuery.Tags

Semantics: The tags the item carries.
