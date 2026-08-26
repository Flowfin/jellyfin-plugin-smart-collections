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
    ///
    /// Where two documents declare one id, the first in that order keeps it and every later one is
    /// a rejection. Which of them is first is therefore decided by the file names rather than by
    /// the file system, so the same directory refuses the same document on every run and on either
    /// server line. Refusing both instead would take a working collection away from an operator
    /// who copied a file, and the pair cannot both be loaded: they claim one collection.
    /// </remarks>
    /// <returns>The documents that loaded and the files that were refused.</returns>
    public RuleDocumentScan Scan()
    {
        var loaded = new List<LoadedRuleDocument>();
        var rejected = new List<RejectedRuleDocument>();

        // Ordinal, because an id is compared and never folded, and because the validator has
        // already refused every id this dictionary could be asked to fold: the set an id is made
        // of has one case. A comparer that folded would agree with it today and stop agreeing the
        // day the set widens, which is the kind of drift nobody re-reads a dictionary to find.
        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);

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

            if (validation.Document is not { } document)
            {
                rejected.Add(new RejectedRuleDocument(name, validation.Errors));
                continue;
            }

            if (claimed.TryGetValue(document.Id, out var holder))
            {
                rejected.Add(new RejectedRuleDocument(
                    name,
                    [
                        new RuleValidationError(
                            "/" + RuleDocumentValidator.IdMember,
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"The id {document.Id} is already held by {holder}{RuleDocumentStore.Extension}, which was read first. Two rules carrying one id are two rules claiming one collection, so the second is refused rather than allowed to take over what the first owns. Give this document an id of its own; a name may be shared, an id may not."))
                    ]));

                continue;
            }

            claimed.Add(document.Id, name);
            loaded.Add(new LoadedRuleDocument(name, document));
        }

        return new RuleDocumentScan(loaded, rejected);
    }
}
