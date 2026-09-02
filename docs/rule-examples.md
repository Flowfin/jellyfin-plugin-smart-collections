# Worked rule documents

Every document on this page is complete. Copy one into the rules directory,
change a value, and it is a collection. Nothing here is a fragment, and nothing
here is written in a vocabulary the plugin does not read.

Each section below carries a marker line of the form `## Example: <title>`, a
sentence saying what an operator gets out of it, and exactly one fenced `json`
block holding the whole document. `RuleExampleDocumentTests` holds the page to
the engine: every document on it is handed to `RuleDocumentValidator`, the same
type the rules directory scan hands a file to, and an example the plugin would
refuse reds the suite with the pointer and the message the refusing stage wrote.

## Why the validator and not the schema

`rule-document.schema.json` is what an editor points at, and it declares the
envelope. A comparison against it alone passes any document carrying the four
envelope members, whatever the rule inside says, so an example naming a field no
table declares and an operator this plugin refuses by name would sit here
validating. The validator reads the rule as well, stage by stage, so it is what
these documents are held to.

## What these examples are and are not

They teach the SHAPE. Every composition group appears in at least one document
below and every item kind the table declares appears in at least one, and those
two are held by the suite rather than by this sentence. The field vocabulary and
the operator set are not: writing every one of them out here would be a second
copy of `rule-fields.md` and `rule-operators.md`, which are the exhaustive lists
and are each held to their own table in both directions.

Those two pages are where a name is looked up. This page is where the assembled
document is read.

## What no example here writes

A member nothing reads. The validator reads `schemaVersion`, `id`, `name`,
`collects` and `match`, and a document may carry anything else without being
refused for it, so an example carrying a member the engine ignores would show a
reader a rule doing something it does not do. An order and a limit are the two
worth naming: the front page's example carries both as the shape they are
planned in, and neither is declared or read yet.

User state is absent for a different reason, and it is a decision rather than an
absence: whether an item has been watched, and by whom, is refused for the first
version of the format. `rule-language.md` carries that refusal with its reason.

## Example: Recently added films

The collection an operator opens to see what arrived this month. `withinLast`
takes a duration rather than a date, so the document does not go stale.

```json
{
    "schemaVersion": 1,
    "id": "recently-added-films",
    "name": "Recently Added Films",
    "collects": ["movie"],
    "match": {
        "allOf": [{ "field": "dateAdded", "operator": "withinLast", "value": "P30D" }]
    }
}
```

## Example: Films from the nineteen-nineties

A decade is two comparisons against the same field rather than one range
operator, which is why `allOf` holds two conditions naming `productionYear`.

```json
{
    "schemaVersion": 1,
    "id": "nineties-films",
    "name": "Films of the 1990s",
    "collects": ["movie"],
    "match": {
        "allOf": [
            { "field": "productionYear", "operator": "greaterThanOrEqual", "value": 1990 },
            { "field": "productionYear", "operator": "lessThanOrEqual", "value": 1999 }
        ]
    }
}
```

## Example: Documentary and history, films and series together

`anyOf` is the group for "either of these", and `collects` naming two kinds is
what puts films and series in one collection. A rule collects the kinds it
declares and never every kind it could.

```json
{
    "schemaVersion": 1,
    "id": "documentary-and-history",
    "name": "Documentary and History",
    "collects": ["movie", "series"],
    "match": {
        "anyOf": [
            { "field": "genres", "operator": "contains", "value": "Documentary" },
            { "field": "genres", "operator": "contains", "value": "History" }
        ]
    }
}
```

## Example: Long films the community rates highly

Two conditions over two different types. `communityRating` holds a decimal and
`runtime` holds a duration, and each operator is one its field's own row
accepts.

```json
{
    "schemaVersion": 1,
    "id": "long-and-well-rated",
    "name": "Long and Well Rated",
    "collects": ["movie"],
    "match": {
        "allOf": [
            { "field": "communityRating", "operator": "greaterThanOrEqual", "value": 8.0 },
            { "field": "runtime", "operator": "greaterThanOrEqual", "value": "PT2H" }
        ]
    }
}
```

## Example: Films classified for a family evening

`in` is equality against several values and is written with a list, which is the
difference between it and `equals`. The classifications are the strings the
library holds, so a server using a different classification system writes its
own.

```json
{
    "schemaVersion": 1,
    "id": "family-evening",
    "name": "Family Evening",
    "collects": ["movie"],
    "match": {
        "allOf": [{ "field": "officialRating", "operator": "in", "value": ["G", "PG"] }]
    }
}
```

## Example: Recent crime or mystery, nothing tagged for exclusion

The document that shows the shape of a nested rule. `allOf` holds a condition, an
`anyOf` and a `noneOf`, so all three groups are in one place and the nesting is
visible. `noneOf` is how a rule excludes: there is no negation operator, and
`rule-composition.md` carries the reason.

```json
{
    "schemaVersion": 1,
    "id": "recent-crime-or-mystery",
    "name": "Recent Crime and Mystery",
    "collects": ["movie", "series"],
    "match": {
        "allOf": [
            { "field": "dateAdded", "operator": "after", "value": "2026-01-01" },
            {
                "anyOf": [
                    { "field": "genres", "operator": "contains", "value": "Crime" },
                    { "field": "genres", "operator": "contains", "value": "Mystery" }
                ]
            },
            {
                "noneOf": [{ "field": "tags", "operator": "contains", "value": "exclude-from-collections" }]
            }
        ]
    }
}
```
