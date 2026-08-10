using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// Reads every rule document in the rules directory and reports what loaded and what did not.
/// </summary>
/// <remarks>
/// This is the type that makes one document per collection worth having. The store gives out one
/// file at a time and the validator answers about one document at a time; neither of them decides
/// what happens to the other files when one of them is broken. The answer is here, and it is that
/// a refused file costs its own collection and nothing else.
///
/// Nothing is written on any path through this type. A refused document is not rewritten, not
/// renamed, not moved and not repaired, so the bytes an operator wrote are the bytes still on
/// disk after a scan that refused them. That matters most exactly when the document is wrong: a
/// plugin that tidies a file it could not read destroys the original in the same motion, and the
/// operator loses the thing they were about to fix.
///
/// This type lives beside the store rather than in the engine because it reads files, which the
/// engine does not do. The invariant lint refuses file access under the engine's path, and the
/// two types this scan produces are in the engine so that evaluation can consume a scan without
/// the dependency running backwards.
/// </remarks>
public sealed class RuleDocumentLoader
{
    private readonly RuleDocumentStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuleDocumentLoader"/> class.
    /// </summary>
    /// <param name="store">The store holding the rule documents.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public RuleDocumentLoader(RuleDocumentStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// Reads every document in the directory once.
    /// </summary>
    /// <remarks>
    /// The order is the store's, which is ordinal by name, so two scans of one directory produce
    /// the same two lists in the same order however the file system chose to enumerate it.
    ///
    /// A directory that does not exist scans as empty rather than as a fault, because a server
    /// that has never had a rule written on it is an ordinary state.
    /// </remarks>
    /// <returns>The documents that loaded and the files that were refused.</returns>
    public RuleDocumentScan Scan()
    {
        var loaded = new List<LoadedRuleDocument>();
        var rejected = new List<RejectedRuleDocument>();

        foreach (var name in _store.ListNames())
        {
            byte[] content;

            try
            {
                content = _store.Read(name);
            }
            catch (ArgumentException)
            {
                // A file whose name the store refuses. Reachable from a directory listing for a
                // file called ".json" or "..json", where taking the extension off leaves nothing
                // that names a document. The condition is not restated here: the store is the one
                // authority on which names it reads, and a second copy of that rule in this loop
                // would be a rule that drifts. What this arm owes is that one such file is a
                // rejection like any other rather than an exception that ends the scan and takes
                // every other collection on the server with it.
                rejected.Add(new RejectedRuleDocument(
                    name,
                    [
                        new RuleValidationError(
                            RuleValidationError.WholeDocument,
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"The file {name}{RuleDocumentStore.Extension} is not one this plugin reads a rule document from, because what is left of its name once the extension is taken off does not name a document."))
                    ]));

                continue;
            }

            var validation = RuleDocumentValidator.Read(content);

            if (validation.Document is { } document)
            {
                loaded.Add(new LoadedRuleDocument(name, document));
            }
            else
            {
                rejected.Add(new RejectedRuleDocument(name, validation.Errors));
            }
        }

        return new RuleDocumentScan(loaded, rejected);
    }
}
