using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Api;

/// <summary>
/// Everything a rule may be written from, read out of the tables that decide it.
/// </summary>
/// <remarks>
/// THIS ENDPOINT IS THE ONE THAT KEEPS THE PAGE HONEST. Without it the page carries its own copy
/// of the field list, the operator list and the group names, and the two drift the first time
/// either moves - which is how a form comes to offer an operator the engine refuses. Every list
/// here is derived from the table that decides it on every request rather than built once and
/// held.
///
/// The schema version bounds are here too, because a page that writes a document has to write one,
/// and the alternative is a number typed into JavaScript.
/// </remarks>
/// <param name="Fields">Every field, in the order the field table declares them.</param>
/// <param name="Operators">Every operator, in the order the operator table declares them.</param>
/// <param name="ItemKinds">Every kind a rule may collect, in the order the kind table declares them.</param>
/// <param name="Groups">The composition group names, in the order the reader declares them.</param>
/// <param name="LowestSchemaVersion">The lowest format version this plugin reads.</param>
/// <param name="CurrentSchemaVersion">The highest format version this plugin reads.</param>
/// <param name="MaximumNestingDepth">How deeply a rule's groups may nest.</param>
public sealed record Vocabulary(
    IReadOnlyList<VocabularyField> Fields,
    IReadOnlyList<VocabularyOperator> Operators,
    IReadOnlyList<VocabularyItemKind> ItemKinds,
    IReadOnlyList<string> Groups,
    int LowestSchemaVersion,
    int CurrentSchemaVersion,
    int MaximumNestingDepth);
