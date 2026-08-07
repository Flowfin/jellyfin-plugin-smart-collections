using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// An error is read in two places that cannot share a rendering: a log line, which has only text,
/// and a form, which needs the pointer as a value. These tests hold the one line form, because it
/// is the one that reaches a person who is not looking at the document.
/// </summary>
public class RuleValidationErrorTests
{
    [Fact]
    public void AnErrorAtAMemberNamesTheMember()
    {
        var error = new RuleValidationError("/schemaVersion", "It is a string and has to be an integer.");

        Assert.Equal("/schemaVersion: It is a string and has to be an integer.", error.ToString());
    }

    /// <summary>
    /// An empty pointer refers to the whole document, which is correct as a value and unreadable
    /// as a line: "": the document is not JSON" reads as a missing field rather than as the whole
    /// file. The line form says so in words instead.
    /// </summary>
    [Fact]
    public void AnErrorAgainstTheWholeDocumentSaysSoRatherThanShowingAnEmptyPointer()
    {
        var error = new RuleValidationError(RuleValidationError.WholeDocument, "The document is not JSON.");

        Assert.Equal("<document>: The document is not JSON.", error.ToString());
    }
}
