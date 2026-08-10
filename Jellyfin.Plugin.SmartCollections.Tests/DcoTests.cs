using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The sign-off gate refuses a commit and then tells the contributor which two documents to
/// read. A contributor meets that message at the worst moment, so the documents have to be
/// there and the certificate has to be the one they are being asked to certify. Neither is
/// something the build or the gate itself would notice.
/// </summary>
public class DcoTests
{
    /// <summary>
    /// SHA-256 of the Developer Certificate of Origin 1.1 as published at
    /// <c>https://developercertificate.org/</c>, taken over the text with LF line endings and a
    /// trailing newline. The document forbids changing its own wording, so a digest is the
    /// check rather than a search for a few phrases: any edit anywhere moves it.
    /// </summary>
    private const string PublishedDigest =
        "f7ac75b443f4ca16b503241344b41aeff9503b0c30bedc2b119551d83cb0fa90";

    /// <summary>
    /// The closing sentence of the sign-off gate's error message, read out of the message
    /// itself rather than out of a copy of it here. Anchoring on <c>::error::</c> keeps this
    /// from matching some other sentence that happens to say "See", and the quote class stops
    /// the match running past the end of the shell string the message lives in.
    /// </summary>
    private static readonly Regex SeeClause = new(
        "::error::[^\"]*See (?<first>\\S+) and (?<second>\\S+?)\\.",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the sign-off workflow as a file on disk. The gate runs from the tracked copy, so
    /// the tracked copy is what is read.
    /// </summary>
    /// <returns>The workflow's text.</returns>
    private static string SignOffWorkflow()
        => File.ReadAllText(
            Path.Combine(RepositoryFiles.Root(), ".github", "workflows", "dco.yml"));

    [Fact]
    public void DcoTextIsThePublishedVersion11()
    {
        var text = RepositoryFiles.ReadFromRoot("DCO")
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(
            PublishedDigest,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }

    [Fact]
    public void EveryDocumentTheSignOffErrorNamesResolves()
    {
        var match = SeeClause.Match(SignOffWorkflow());

        Assert.True(match.Success, "The sign-off error message names no documents to read.");

        foreach (var named in new[] { "first", "second" }.Select(group => match.Groups[group].Value))
        {
            var resolved = Path.Combine(RepositoryFiles.Root(), named.TrimStart('.', '/'));

            Assert.True(
                File.Exists(resolved),
                "The sign-off error sends a contributor to " + named + ", which is not a file.");
        }
    }
}
