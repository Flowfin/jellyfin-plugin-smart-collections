using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A compiled query asks the server for a kind by NUMBER. No member of the server's own item kind
/// enumeration declares a value, so the number is the member's position in that declaration, and a
/// package built against one line and installed on another asks for whatever sits at that position
/// there. Comparing the two lines as sets would pass while every value after an inserted member
/// had moved, so these tests compare the ordered pairs against a checked-in list.
/// </summary>
/// <remarks>
/// The list lives beside this file rather than in it, because it is evidence read off an assembly
/// on a day rather than a preference: the file carries the commands it was taken with and the
/// bound on what they showed. It is compared against the enumeration THE SUITE IS COMPILED
/// AGAINST, and the suite is compiled twice, so the comparison is made once per supported line.
/// </remarks>
public class RuleItemKindServerSurfaceTests
{
    private const string Expected = "Jellyfin.Plugin.SmartCollections.Tests/base-item-kind.expected.txt";

    private static IReadOnlyList<string> ExpectedPairs()
        => RepositoryFiles.ReadFromRoot(Expected)
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0 && line[0] != '#')
            .ToArray();

    private static IReadOnlyList<string> ResolvedPairs()
        => Enum.GetValues<BaseItemKind>()
            .Select(kind => ((int)kind).ToString(CultureInfo.InvariantCulture) + " " + kind.ToString())
            .ToArray();

    /// <summary>
    /// Without this the comparison below passes on a file somebody emptied, because two empty
    /// lists agree.
    /// </summary>
    [Fact]
    public void TheExpectedListIsInTheTreeAndCarriesPairs()
    {
        Assert.True(
            File.Exists(Path.Combine(RepositoryFiles.Root(), Expected)),
            Expected + " is not in the tree.");

        Assert.NotEmpty(ExpectedPairs());
    }

    /// <summary>
    /// The done condition this test carries, in the stronger form the issue argues for: not that
    /// every accepted name is PRESENT on both lines, which a set comparison answers, but that the
    /// whole enumeration stands where it stood, which is what decides the number a package sends.
    /// </summary>
    [Fact]
    public void TheServersEnumerationIsWhereTheExpectedListSaysItIs()
        => Assert.Equal(ExpectedPairs(), ResolvedPairs());

    /// <summary>
    /// The narrower clause the done condition states, asserted rather than inferred from the list
    /// above: every kind this plugin accepts resolves to a member the line the suite is compiled
    /// against actually declares.
    /// </summary>
    [Fact]
    public void EveryAcceptedKindNamesAMemberThisLineDeclares()
    {
        var declared = Enum.GetValues<BaseItemKind>().ToHashSet();

        foreach (var row in RuleItemKindTable.Rows)
        {
            Assert.Contains(row.ServerKind, declared);
            Assert.Contains(
                ((int)row.ServerKind).ToString(CultureInfo.InvariantCulture) + " " + row.ServerKind.ToString(),
                ExpectedPairs());
        }
    }

    /// <summary>
    /// The names this plugin writes are the server's own, lowercased. That is a claim the page
    /// makes about every row, and a row spelling a kind differently from the library an operator
    /// is looking at is the kind of drift nobody reports as a bug.
    /// </summary>
    [Fact]
    public void EveryWrittenNameIsTheServersOwnNameLowercased()
    {
        foreach (var row in RuleItemKindTable.Rows)
        {
            Assert.Equal(row.ServerKind.ToString().ToLowerInvariant(), row.Name);
        }
    }
}
