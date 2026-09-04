# What the server's query answers, and what it does not

A rule is compiled into the query the server's own item store already answers,
rather than evaluated item by item in the plugin. Which comparisons that query
can carry is declared in `RuleQueryTable`, and this page is that table written
out.

Each section below carries a marker line of the form `## Pair: <field>
<operator>`, a `Writes:` line naming the property on `InternalItemsQuery` the
pair narrows, or both properties joined by `and` where a pair writes two, and a
`Semantics:` line saying what the query is asked once the pair has written to
it. `RuleQueryDocumentTests` holds the page to the table in
both directions, so a pair compiled without a section reds the suite and a
section describing a pair that is not compiled reds it too.

## Why the query rather than the plugin

The existing plugins in this space ask the server for everything and filter in
the plugin. Items are projected onto a class, a predicate is compiled per
condition and run per item, and the cost of the expensive fields is carried by
hand-written caches and a tiered extraction scheme. That cost is paid on every
item on every run, and it grows with the library rather than with the rule.

Asking the server for a narrowed set moves the work to the thing already built
to do it. What it costs instead is this page: not every comparison a rule may
write is one the query can express, and where a comparison is missing the
difference has to be visible rather than discovered.

## A pair is a field AND an operator

The field table names the property a field is about. This table is narrower,
because a field the query knows still has operators the query cannot express.
`name` reaches the library through `Name`, and the query offers no way to ask
for a title that ends with something, so `name equals` is here and
`name endsWith` is not.

A condition whose pair is not here is handed back by the compiler rather than
dropped, and the stage that answers it is declared separately. A caller that
asked the query and ignored what was handed back would return items the rule
does not match, which is why those conditions are part of the compiler's answer
rather than a footnote to it.

## What the comparison behind a row is, and what it is not

Every row here was read off the server's own translation of the query rather
than off the property's name, at the older of the two lines this plugin ships
for, which is the line that bounds what may be written:

```
gh api "repos/jellyfin/jellyfin/contents/Jellyfin.Server.Implementations/Item/BaseItemRepository.cs?ref=v10.11.11" \
  --jq .content | base64 -d | grep -nE 'MinCommunityRating|MinDateCreated|MinPremiereDate|MaxPremiereDate|filter.Genres|filter.OfficialRatings|filter.Years'
```

THE COMPARISON IS THE SERVER'S AND NOT AN ORDINAL ONE. The server compares its
own cleaned form of a name, a genre and a tag, so `name equals` is the server's
equality over titles rather than a comparison of bytes. A row being on this page
says the query answers the comparison, and says nothing about which string
comparison this plugin names everywhere, which is a question of its own.

## Two comparisons the query expresses only by an offset

`after` and `before` are strict: the operator set's own sentences are that the
field is later than the value and that it is earlier than it. The properties
that carry them compare with at-or-after and at-or-before.

The translation is exact rather than approximate, because an instant here is a
whole number of ticks: `t > x` and `t >= x + 1 tick` select the same instants.
So `after` writes the value plus one tick and `before` writes the value minus
one, and the boundary instant the operator excludes is excluded by the query
rather than by anything after it.

The last tick a date can name has no room for the offset, and neither has the
first. A document naming either is accepted by this plugin and cannot be carried
by these properties, so that condition is handed back rather than narrowed.

## One pair reads the instant the evaluation was given

`withinLast` is the one operator whose value is a length of time rather than an
instant, so the span it names has to end somewhere, and where it ends is the
instant the compiler is handed. That instant is an argument to the compiler
rather than a clock it reads: whatever runs an evaluation resolves it once and
passes that one value in, so two relative conditions in one rule see one
instant, and the same rule compiled twice at one instant is one query. The
engine reads no clock, and `ambient-clock-in-the-engine` in the invariant lint
refuses one arriving.

The pair writes both bounds of the span. The floor is the instant less the span
and the ceiling is the instant itself, both inclusive, because the operator's
sentence is not a strict one and the properties compare with at-or-after and
at-or-before. So no tick offset is owed here, unlike the two strict operators
above.

A span longer than the time between the first instant a date can name and the
evaluation's own is the one document these properties cannot carry, because the
floor it asks for is before any instant the query can hold. That condition is
handed back rather than clamped, for the reason every hand-back on this page
gives.

The pair claims both properties it writes, so `premiereDate withinLast` beside
`premiereDate before` is refused on the ceiling the two share, and beside
`premiereDate after` on the floor, the same way two conditions writing one
property are refused below.

## One range the query cannot carry

A production year is a whole number, which this plugin reads to the full range
of a 64-bit integer, and the query holds years as 32-bit integers. A document
naming a year outside that range is handed back for the same reason: a narrowing
that is quietly not applied is a rule that means something else.

## Two conditions writing one property are refused

The query holds one value per property, so two conditions that both narrow it
cannot both be written. The second write would replace the first, the query
would ask half the rule, and nothing would say so.

Refused rather than combined, and the choice is between two defensible readings
rather than between a right one and a wrong one. Two years written with `equals`
could be read as a rule matching neither, since both conditions have to hold, or
as the list the query's own year array would make of them. Neither reading is
what the document says, so neither is chosen quietly.

The refusal is per property rather than per field, because the property is where
the replacement would happen. `tags contains` and `tags notContains` write two
different properties and are both compiled.

## Pair: communityRating greaterThanOrEqual

Writes: InternalItemsQuery.MinCommunityRating

Semantics: The community rating is the value or above it.

## Pair: dateAdded after

Writes: InternalItemsQuery.MinDateCreated

Semantics: The server first saw the item after the value.

## Pair: genres contains

Writes: InternalItemsQuery.Genres

Semantics: The item carries the genre.

## Pair: name equals

Writes: InternalItemsQuery.Name

Semantics: The title is the value.

## Pair: officialRating equals

Writes: InternalItemsQuery.OfficialRatings

Semantics: The age classification is the value.

## Pair: officialRating in

Writes: InternalItemsQuery.OfficialRatings

Semantics: The age classification is one of the values.

## Pair: premiereDate after

Writes: InternalItemsQuery.MinPremiereDate

Semantics: The item was first released after the value.

## Pair: premiereDate before

Writes: InternalItemsQuery.MaxPremiereDate

Semantics: The item was first released before the value.

## Pair: premiereDate withinLast

Writes: InternalItemsQuery.MinPremiereDate and InternalItemsQuery.MaxPremiereDate

Semantics: The item was first released inside the span that ends at the instant the evaluation was given.

## Pair: productionYear equals

Writes: InternalItemsQuery.Years

Semantics: The production year is the value.

## Pair: productionYear in

Writes: InternalItemsQuery.Years

Semantics: The production year is one of the values.

## Pair: tags contains

Writes: InternalItemsQuery.Tags

Semantics: The item carries the tag.

## Pair: tags notContains

Writes: InternalItemsQuery.ExcludeTags

Semantics: The item carries the tag nowhere.

## What is not on this page

Which conditions of a rule all have to hold is the composition tree's question
and not this one's. A server query is a conjunction, so only conditions that all
have to hold can be pushed into it, and the compiler here is handed a flat list
rather than the tree.

`dateAdded withinLast` has no row although the query carries the floor that
field names, and it is the one pair on the other side of this boundary for a
reason that is the server's rather than this plugin's. The span ends at the
instant the evaluation was given, so writing it exactly takes a floor and a
ceiling, and the query carries a ceiling for a premiere date and none for the
date the server first saw an item. Read off the query type at both lines this
plugin ships for:

```
for ref in v10.11.11 v12.0-rc4; do
  gh api "repos/jellyfin/jellyfin/contents/MediaBrowser.Controller/Entities/InternalItemsQuery.cs?ref=$ref" \
    --jq .content | base64 -d | grep -oE 'public DateTime\? (Min|Max)(DateCreated|PremiereDate)'
done
public DateTime? MinPremiereDate
public DateTime? MaxPremiereDate
public DateTime? MinDateCreated
public DateTime? MinPremiereDate
public DateTime? MaxPremiereDate
public DateTime? MinDateCreated
```

A floor alone would ask the server for everything from the start of the span
onward, which is a superset of what the sentence says, and a narrowing that
means something else is the one thing this page may not describe. So
`dateAdded withinLast` is handed back like any other pair with no row, and it
stays handed back until the server carries a ceiling for that date or until the
stage that answers what is handed back exists to finish it. `RuleQueryCompilerTests`
holds the hand-back to that reading rather than to this sentence: the test
asserts the query type carries no `MaxDateCreated`, so the day a server line
adds one the suite says the pair can compile.
