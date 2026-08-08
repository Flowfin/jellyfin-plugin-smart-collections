using System.IO;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The seeds the fuzzer starts from, held to the contract the fuzz target asserts (#18).
/// </summary>
/// <remarks>
/// The fuzz harness catches nothing, because <see cref="RuleDocumentValidator.Read"/> is meant to
/// answer with a result for every input rather than throw. That property is what turns an
/// exception in a fuzzing run into a finding instead of an expected outcome, and it is asserted
/// here so it is checked on every ordinary run rather than only on the scheduled one.
///
/// Reading the seeds from disk rather than restating them here is the point. A seed edited into a
/// shape the parser throws on would otherwise make the next fuzzing run report a crash that is the
/// harness's fault, and nobody would learn that until the run happened.
///
/// The directory is found by walking up to the repository root, which is what keeps this test
/// working from whatever directory the runner starts it in, with no display, no server and no
/// elevated rights.
/// </remarks>
public class FuzzCorpusTests
{
    private static string CorpusDirectory()
        => Path.Combine(RepositoryFiles.Root(), "tools", "fuzz", "corpus");

    [Fact]
    public void TheCorpusIsNotEmpty()
    {
        // A fuzzer given an empty seed directory refuses to start, and a test that iterated an
        // empty list would pass while saying nothing. This is the guard against both.
        Assert.NotEmpty(Directory.GetFiles(CorpusDirectory()));
    }

    [Fact]
    public void EverySeedIsAnsweredRatherThanThrownOn()
    {
        foreach (var path in Directory.GetFiles(CorpusDirectory()))
        {
            var text = File.ReadAllText(path);

            // Not Assert.NotNull on a result: what is being asserted is that the call returns at
            // all. An exception here fails the test with the seed's own path in the message,
            // which is the thing somebody needs in order to fix it.
            var result = RuleDocumentValidator.Read(text);

            Assert.True(
                result.IsValid || result.Errors.Count > 0,
                Path.GetFileName(path) + " was neither accepted nor refused with a reason.");
        }
    }

    [Fact]
    public void NoSeedIsEmpty()
    {
        // A zero-length seed is skipped by the fuzzer rather than used, so one sitting in the
        // directory is a seed nobody is getting the benefit of.
        foreach (var path in Directory.GetFiles(CorpusDirectory()))
        {
            Assert.True(
                new FileInfo(path).Length > 0,
                Path.GetFileName(path) + " is empty, and an empty seed is skipped rather than used.");
        }
    }
}
