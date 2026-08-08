using System.IO;
using Jellyfin.Plugin.SmartCollections.Rules;
using SharpFuzz;

namespace Jellyfin.Plugin.SmartCollections.Fuzz;

/// <summary>
/// Feeds arbitrary bytes to the rule document parser (#18).
/// </summary>
/// <remarks>
/// A rule document is the plugin's only untrusted input surface. An operator writes it, the
/// plugin parses it and validates it, and every byte of it comes from outside the code. A parser
/// that has only ever seen the documents its own tests wrote is a parser nobody has tried to
/// break.
///
/// NOTHING IS CAUGHT HERE, and that is the property being asserted rather than an omission.
/// <see cref="RuleDocumentValidator.Read"/> answers with a result in every case it is designed
/// for, including a document that is not JSON, so an exception leaving it is a finding rather
/// than an expected outcome. A harness that swallowed one would report a clean run over a parser
/// that throws, which is the failure this file exists to avoid. A crasher is triaged as its own
/// finding with its own fix, never patched here.
///
/// The bytes are decoded as UTF-8 before they reach the parser, because the parser takes text.
/// Decoding never throws on invalid bytes: they become replacement characters, so a mutation the
/// fuzzer made in the middle of a multi-byte sequence still reaches the parser rather than
/// stopping in the decoder.
/// </remarks>
internal static class Program
{
    private static void Main()
    {
        Fuzzer.Run(stream =>
        {
            using var reader = new StreamReader(stream);
            RuleDocumentValidator.Read(reader.ReadToEnd());
        });
    }
}
