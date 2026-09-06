# The order a rule declares, and the cap it may put on the collection

A collection has an order whether or not a document declares one. Without a
`sort` the order is the item identifier and nothing else: total, reproducible,
and meaningless to a person. With one, the order is the document's, and the
identifier stays underneath it as the tie-break.

Both members are optional and both are read by `RuleSortReader`. The directions
and the fields that may be ordered by are declared in `RuleSortTable`, and
`RuleSortDocumentTests` holds this page to that table in both directions, so a
direction added without a section reds the suite and a section describing one
that does not exist reds it too.

## What it looks like

```json
{
    "schemaVersion": 1,
    "id": "recent-heists",
    "name": "Recent heists",
    "collects": ["movie"],
    "sort": [
        { "field": "premiereDate", "direction": "descending" },
        { "field": "name", "direction": "ascending" }
    ],
    "limit": 25,
    "match": {
        "allOf": [{ "field": "tags", "operator": "contains", "value": "heist" }]
    }
}
```

The terms are read in the order they are written: the first decides, the second
decides what the first left tied, and so on. That is the one place in a rule
document where the order of what you wrote changes the answer - a scope is a
set and two documents naming one scope in two orders compile to one query, and
two documents naming one pair of sort terms in two orders do not.

## Every sort ends with the identifier

Every term can tie. Two films released on one day, two series with one name,
twenty films with one age classification. An order that stopped at the declared
terms would therefore be partial, and a partial order over a set the server may
answer in any sequence produces a different collection on two runs of one rule
against one library.

So the identifier is appended to every declared order, and it can never tie,
which makes the order total. It is not a term you write and it cannot be left
out.

## An item the library holds no value for sorts last

In both directions. The alternative is to treat absence as a value smaller than
every other, which reverses along with the direction and puts every film with no
premiere date at the top of a descending order - a collection whose first
screenful is the items the rule knows least about.

Two items that both hold nothing for the field are equal under that term, so the
term after it decides, and where there is none the identifier does.

## Direction: ascending

Smallest first: the earliest instant, the lowest number, the shortest length,
and text in the order an ordinal comparison puts it.

Text is compared ordinally rather than by the server's language, because a
collection ordered by name has to come out the same on a server in Ankara as on
one in Reykjavik, and a culture-aware comparison is the one thing that
guarantees it does not.

## Direction: descending

Largest first, which is the reverse of `ascending` over the items that carry a
value. It does not move the items that carry none.

## Fields with no order

A term names a field whose value on an item is one comparable thing. A field
whose value is a LIST has no order: an item carrying three genres has no place
in an order over genres unless something invents one, and the inventions
available - the first member, the shortest, the list joined into one string -
are each a different order and none of them is what the document says.

Two fields are refused for that reason, and a term naming either of them is
refused with a message naming the fields that can be ordered by.

### No order: genres

An item carries any number of genres.

### No order: tags

An item carries any number of tags.

## The cap

`limit` is the greatest number of items the collection holds. It is a whole
number of one or more; a rule meant to collect nothing is not one anybody writes
on purpose.

**A cap written without a sort is refused.** The first fifty of a set nobody
ordered are the items whose identifiers happen to sort first, which is
reproducible and is not a thing anybody means. The two repairs are each one
line: declare the order the cap applies to, or drop the cap.

**The cap counts what the rule collects, not what the server answered.** It is
applied after the order and after every condition, including the conditions the
server's own query could not carry. A cap pushed into the query would cut the
answer before those conditions had been compared, so a rule asking for fifty
films would collect however many of the server's first fifty survived the stage
after the query. `rule-queries.md` is where that boundary is written out.

## What this deliberately cannot say

A random order. A shuffled collection is a reasonable thing to want and it is
the opposite of a reproducible one, so it is not in this version. If it arrives
later it arrives with a seed stored in the rule, so that "random" is still the
same answer twice.
