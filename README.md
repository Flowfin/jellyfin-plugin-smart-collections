> [!NOTE]
>
> **Part of [Flowfin](https://github.com/Flowfin).** It works with any Jellyfin
> server, and with the Flowfin clients.

# Jellyfin Smart Collections

Collections from a rule engine over Jellyfin library metadata.

A collection in Jellyfin is a list somebody has to keep. This plugin makes it a
list somebody describes once. You write a rule, the plugin evaluates it against
the library, and the collection it owns holds exactly the items the rule
matches. When the library changes, the collection follows.

## Status

The plugin is not finished and is not published to any catalogue. What is
described below is the design the repository is being built to, and the tracker
carries the work that has to land before any of it runs on a server. Nothing
here is a promise about a release date.

Do not install this on a server you care about yet.

## Which server versions it runs on

Two lines are supported, and each one gets its own package, because they host
different runtimes:

| Server line    | Runtime the server hosts |
| -------------- | ------------------------ |
| Jellyfin 10.11 | .NET 9                   |
| Jellyfin 12.0  | .NET 10                  |

That table is read from the server's own project file rather than from
documentation:

```
gh api "repos/jellyfin/jellyfin/contents/Jellyfin.Server/Jellyfin.Server.csproj?ref=v10.11.11" \
  --jq .content | base64 -d | grep TargetFramework
    <TargetFramework>net9.0</TargetFramework>
gh api "repos/jellyfin/jellyfin/contents/Jellyfin.Server/Jellyfin.Server.csproj?ref=v12.0-rc4" \
  --jq .content | base64 -d | grep TargetFramework
    <TargetFramework>net10.0</TargetFramework>
```

A package built for one line does not load on the other, and installing the
wrong one is not a subtle failure: the server refuses the assembly.

## Installing

There is no catalogue entry yet, so the only route today is to build from a
clone and copy the output into the server's plugin directory:

```
git clone https://github.com/Flowfin/jellyfin-plugin-smart-collections
cd jellyfin-plugin-smart-collections
dotnet build -c Release
```

Copy the built assembly into a directory of its own under the server's
`plugins` path, then restart the server. Where that path is depends on how the
server was installed, and the server's own documentation is the authority for
it.

Once a catalogue entry exists, this section will name the repository URL to add
and this paragraph will go away.

## What a rule looks like

A rule is one JSON document per collection, held in the plugin's data
directory. It says which item kinds it collects, what has to be true of an item
for it to belong, and how the collection is ordered.

```json
{
    "schemaVersion": 1,
    "id": "nineties-thrillers",
    "name": "Nineties Thrillers",
    "collects": ["Movie"],
    "match": {
        "all": [
            { "field": "genres", "operator": "contains", "value": "Thriller" },
            { "field": "productionYear", "operator": "atLeast", "value": 1990 },
            { "field": "productionYear", "operator": "atMost", "value": 1999 }
        ]
    },
    "order": { "field": "sortName", "direction": "ascending" },
    "limit": 200
}
```

Saved into the rule directory, that produces a collection called
`Nineties Thrillers` holding every film in the library whose genres include
`Thriller` and whose production year falls in the nineties, ordered by sort
name, capped at two hundred items.

Reading it clause by clause:

- `schemaVersion` is what lets a later version of the plugin read this document
  without guessing at it. A document without one is refused rather than
  interpreted, and a document from a version the plugin does not know is refused
  with both numbers in the message.
- `id` is the rule's identity and does not change when the name does. It is what
  the plugin stamps on the collection it owns, so renaming a collection does not
  orphan it.
- `collects` names the item kinds the rule gathers.
- `match` is a tree of conditions. `all` requires every clause to hold. Which
  composition operators exist and how deeply they may nest is part of the rule
  language rather than something a document decides for itself.
- `field` and `operator` come from a declared table rather than from whatever
  property happens to exist on some class, so an unknown field is a validation
  message naming the legal ones and not an exception at evaluation time.
- `order` and `limit` go together. A limit without a total order is a collection
  whose contents change between two runs over an unchanged library, so the order
  is defined down to the last tie.

The field table, the operator set and the exact JSON schema are being settled on
the tracker under the rule language milestone. Until that lands, the document
above is the shape rather than a contract, and this section is the front page
rather than the reference. The full reference is planned separately.

## What a rule deliberately cannot say

Some of the language's limits are choices rather than gaps, and they are worth
knowing before you write a rule that wants them.

There are no regular expressions. A pattern supplied by an operator and
evaluated on a server task thread can be made to run for an unbounded time, and
this plugin does not put that on your server.

There is no per-user state. A Jellyfin collection is server-wide and every
account sees the same one, so a rule about what one person has watched or
favourited would build a list everyone sees out of one person's viewing.

Items are not pinned. Membership comes from the rule, so an item added to a
generated collection by hand is removed the next time the rule runs.

Each of these is recorded with its reasoning on the tracker rather than settled
in this file, and any of them can be revisited there.

## Reporting something

Bug reports and feature requests go through the issue templates, which ask for
the server version, the plugin version and the rule document that reproduced the
problem. Without those three a report about a collection cannot be acted on.

Anything with a security dimension goes through [SECURITY.md](SECURITY.md)
instead, which names a private route.

## Licence

GPL-3.0. See [LICENSE](LICENSE).
