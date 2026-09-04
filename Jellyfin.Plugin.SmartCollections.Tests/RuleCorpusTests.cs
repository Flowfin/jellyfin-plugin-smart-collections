using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// Every document in the corpus, compared against the answer checked in beside it.
/// </summary>
/// <remarks>
/// A unit test over one stage proves that stage works. It does not prove that a change left the
/// ANSWERS alone, because a test written alongside a change tends to agree with it. What this
/// file compares is a rendering of what each document compiles to against a file that was in the
/// tree before the change, so a moved answer has to move an expected file in the same commit and
/// a reader is asked whether the move was intended.
///
/// THE REGENERATION IS ONE COMMAND AND IT LEAVES THE SUITE RED. Setting
/// <see cref="RewriteVariable"/> makes the comparison below write each file instead of asserting
/// against it, and <see cref="TheExpectedFilesWereComparedRatherThanRewritten"/> then fails, so a
/// regeneration cannot be mistaken for a green run and an unintended change cannot repair itself
/// on a route that happens to have the variable set. <c>docs/testing.md</c> carries the command.
///
/// THE VOCABULARY IN THE CORPUS IS A FIXTURE VOCABULARY. The genres, tags, names and ratings the
/// documents write exist to reach a pair in the query table, and no test here asserts anything
/// about this repository's own state.
/// </remarks>
public class RuleCorpusTests
{
    /// <summary>
    /// The environment variable that turns the comparison into a rewrite.
    /// </summary>
    public const string RewriteVariable = "SMART_COLLECTIONS_REWRITE_EXPECTED";

    /// <summary>
    /// The corpus document names, as xunit member data.
    /// </summary>
    /// <returns>One row per document.</returns>
    public static TheoryData<string> Documents()
    {
        var data = new TheoryData<string>();

        foreach (var name in RuleCorpus.Names())
        {
            data.Add(name);
        }

        return data;
    }

    private static bool RewriteRequested
        => string.Equals(
            Environment.GetEnvironmentVariable(RewriteVariable),
            "1",
            StringComparison.Ordinal);

    private static string[] Lines(string path)
        => File.ReadAllText(path)
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToArray();

    // The leading run of comment lines, which says what the document beside it is for. It is the
    // one part of an expected file a person writes, so a rewrite carries it forward rather than
    // deleting the sentence that explains why the file exists.
    private static string[] Header(string name)
        => Lines(RuleCorpus.ExpectedPath(name))
            .TakeWhile(line => line.StartsWith('#'))
            .ToArray();

    private static string[] Body(string name)
        => Lines(RuleCorpus.ExpectedPath(name))
            .SkipWhile(line => line.StartsWith('#'))
            .Where(line => line.Length > 0)
            .ToArray();

    /// <summary>
    /// Without this every comparison below passes over an empty corpus, because two empty lists
    /// agree. The dozen is the number the done condition names.
    /// </summary>
    [Fact]
    public void TheCorpusHoldsAtLeastADozenDocuments()
        => Assert.True(
            RuleCorpus.Names().Length >= 12,
            "The corpus holds " + RuleCorpus.Names().Length + " documents.");

    /// <summary>
    /// A document with no expected file is compared against nothing, and an expected file with no
    /// document is a comparison nobody makes. Both directions, because either one alone leaves a
    /// file in the tree that reads as covered and is not.
    /// </summary>
    [Fact]
    public void EveryDocumentHasAnExpectedFileAndEveryExpectedFileHasADocument()
    {
        var directory = Path.Combine(RepositoryFiles.Root(), RuleCorpus.Directory);

        var expected = Directory.GetFiles(directory, "*.expected.txt")
            .Select(path => Path.GetFileName(path)!.Replace(".expected.txt", string.Empty, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(RuleCorpus.Names(), expected);
    }

    /// <summary>
    /// The header is what tells a later reader why a document is in the corpus, which is the
    /// question a moved expected file makes somebody ask.
    /// </summary>
    /// <param name="name">The document name.</param>
    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryExpectedFileSaysWhatItsDocumentIsFor(string name)
        => Assert.NotEmpty(Header(name));

    /// <summary>
    /// The done condition this test carries: the suite compares every document against its
    /// checked-in expected file on every run.
    /// </summary>
    /// <param name="name">The document name.</param>
    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryExpectedFileHoldsWhatItsDocumentCompilesTo(string name)
    {
        var rendered = RuleCorpus.Render(name);

        if (RewriteRequested)
        {
            File.WriteAllText(
                RuleCorpus.ExpectedPath(name),
                string.Join("\n", Header(name).Concat(rendered)) + "\n");

            return;
        }

        Assert.Equal(Body(name), rendered);
    }

    /// <summary>
    /// A regeneration is always red, by this one test, so it cannot be read as a run that found
    /// nothing. Read the diff the rewrite made, then run the suite again without the variable.
    /// </summary>
    [Fact]
    public void TheExpectedFilesWereComparedRatherThanRewritten()
        => Assert.False(
            RewriteRequested,
            RewriteVariable + " is set, so the expected files were rewritten rather than compared. "
            + "Read the diff and run the suite again without it.");

    /// <summary>
    /// A corpus that reaches two of the pairs the query table declares would be green through a
    /// change to any of the others. This is the guard on the guard.
    /// </summary>
    [Fact]
    public void TheCorpusReachesEveryPairTheQueryTableDeclares()
    {
        var reached = RuleCorpus.Names()
            .SelectMany(RuleCorpus.Conditions)
            .Select(condition => RuleQueryTable.Find(condition.Field.Field, condition.Operator.Operator))
            .Where(row => row is not null)
            .Select(row => row!)
            .ToHashSet();

        var unreached = RuleQueryTable.Rows
            .Where(row => !reached.Contains(row))
            .Select(row => RuleFieldTable.Of(row.Field).Name + " " + RuleOperatorTable.Of(row.Operator).Name)
            .OrderBy(pair => pair, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unreached);
    }

    /// <summary>
    /// The corpus holds documents the read refuses and documents the compiler refuses, not only
    /// documents that compile. A refusal message is an answer somebody reads, and a change can
    /// move one silently.
    /// </summary>
    [Fact]
    public void TheCorpusHoldsARefusedDocumentAndACompilerRefusal()
    {
        var renderings = RuleCorpus.Names().Select(RuleCorpus.Render).ToArray();

        Assert.Contains(renderings, lines => lines.Any(line => line.StartsWith("refused: ", StringComparison.Ordinal)));
        Assert.Contains(renderings, lines => lines.Any(line => line.StartsWith("refused by the compiler: ", StringComparison.Ordinal)));
        Assert.Contains(renderings, lines => lines.Any(line => line.StartsWith("after the query: ", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The command on the page and the variable in this file are one string. A page naming a
    /// variable nothing reads is a regeneration command that silently compares instead.
    /// </summary>
    [Fact]
    public void TheRegenerationCommandOnThePageNamesTheVariableThisSuiteReads()
        => Assert.Contains(RewriteVariable, RepositoryFiles.ReadFromRoot("docs/testing.md"), StringComparison.Ordinal);
}
