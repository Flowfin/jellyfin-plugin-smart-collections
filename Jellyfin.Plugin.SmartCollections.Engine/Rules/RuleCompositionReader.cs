using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// Reads the shape of a rule's composition: which groups hold what, how deep the tree goes, and
/// whether any group is empty.
/// </summary>
/// <remarks>
/// This stage reads the shape and never a condition. What a condition may say is the field
/// vocabulary's business and arrives as its own stage over the same text; a condition here is a
/// place in the document, carried as a JSON Pointer, and nothing about it is judged. Keeping the
/// two apart is what lets a document with a malformed condition still be told from one whose
/// groups are wrong, which are different repairs for the operator.
///
/// Nesting is bounded by <see cref="MaximumNestingDepth"/>. An unbounded tree over untrusted text
/// is a stack the document decides the size of, and a rule nested deeply enough is one nobody can
/// check by reading, which defeats the point of a rule being declared rather than programmed.
/// The bound here is not the only one: <see cref="JsonDocument"/> refuses a document nested past
/// its own default depth before this stage sees it, so a document that reaches here is already
/// bounded and this bound is the readable one rather than the safety one.
///
/// Every reason is collected rather than the first, because a composition is where an operator's
/// typing mistakes collect and repairing them one run at a time is the slowest way to fix a file.
/// </remarks>
public static class RuleCompositionReader
{
    /// <summary>
    /// The deepest a composition may nest, counting the outermost group as one.
    /// </summary>
    /// <remarks>
    /// Four rather than a larger number, and it is a choice rather than a reading of anything.
    /// Four allows a group of groups of groups, which is already at the edge of what somebody can
    /// hold in their head while checking what a rule collects; the issue that asked for this
    /// bound names five as past that edge. Raising it is a one-line change and a test, and the
    /// argument for raising it is the sentence above rather than a preference.
    ///
    /// There is deliberately no setting that raises it on a running server. A limit an operator
    /// can turn off is a limit that is off on the server where it mattered.
    /// </remarks>
    public const int MaximumNestingDepth = 4;

    /// <summary>
    /// The three group members, and what each one means.
    /// </summary>
    private static readonly (string Name, RuleConditionGroupKind Kind)[] Kinds =
    [
        ("allOf", RuleConditionGroupKind.All),
        ("anyOf", RuleConditionGroupKind.Any),
        ("noneOf", RuleConditionGroupKind.None)
    ];

    /// <summary>
    /// Gets the member names a group is written with, in the order a refusal lists them.
    /// </summary>
    public static IReadOnlyList<string> GroupNames { get; } = ["allOf", "anyOf", "noneOf"];

    /// <summary>
    /// Returns the member name a group of a kind is written with.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The name a document writes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> has no name.</exception>
    public static string NameOf(RuleConditionGroupKind kind)
    {
        foreach (var declared in Kinds)
        {
            if (declared.Kind == kind)
            {
                return declared.Name;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(kind), kind, "No member name is declared for this group kind.");
    }

    /// <summary>
    /// Reads a composition, starting at a group.
    /// </summary>
    /// <param name="element">The element the composition begins at.</param>
    /// <param name="pointer">Where that element is in the document, as a JSON Pointer.</param>
    /// <returns>The tree, or every reason it was refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pointer"/> is <see langword="null"/>.</exception>
    public static RuleCompositionRead Read(JsonElement element, string pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);

        var errors = new List<RuleValidationError>();
        var group = ReadGroup(element, pointer, 1, errors);

        return errors.Count > 0 || group is null
            ? RuleCompositionRead.Refused(errors)
            : RuleCompositionRead.Accepted(group);
    }

    private static RuleConditionGroup? ReadGroup(
        JsonElement element,
        string pointer,
        int depth,
        List<RuleValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(
                pointer,
                "A group is a JSON object carrying one of " + string.Join(", ", GroupNames) + "."));
            return null;
        }

        string? name = null;
        var kind = default(RuleConditionGroupKind);
        var declared = 0;

        foreach (var candidate in Kinds)
        {
            if (!element.TryGetProperty(candidate.Name, out _))
            {
                continue;
            }

            declared++;

            if (declared == 1)
            {
                name = candidate.Name;
                kind = candidate.Kind;
            }
        }

        if (declared == 0)
        {
            errors.Add(new RuleValidationError(
                pointer,
                "This object carries none of " + string.Join(", ", GroupNames) + ", so it is not a group."));
            return null;
        }

        if (declared > 1)
        {
            // Refused rather than resolved by an order this code would have to invent. An object
            // carrying two of them is two rules written on top of each other, and whichever one a
            // reader meant, the other one is silently doing something.
            errors.Add(new RuleValidationError(
                pointer,
                "This object carries " + declared + " of " + string.Join(", ", GroupNames)
                + ", and a group carries exactly one. Nest the second one inside the first."));
            return null;
        }

        if (depth > MaximumNestingDepth)
        {
            errors.Add(new RuleValidationError(
                pointer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"This group is nested {depth} deep and a rule nests at most {MaximumNestingDepth}. A rule nested deeper than that is one nobody can check by reading, which is what a declared rule is for.")));
            return null;
        }

        var members = element.GetProperty(name!);

        if (members.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new RuleValidationError(
                pointer + "/" + name,
                "A group holds an array of groups and conditions."));
            return null;
        }

        if (members.GetArrayLength() == 0)
        {
            // Refused rather than read as matching everything or nothing. Both readings are
            // defensible, which is exactly why neither may be chosen quietly: an operator who
            // deleted the last condition out of a group gets a message rather than a collection
            // that silently swallowed their library or emptied itself.
            errors.Add(new RuleValidationError(
                pointer + "/" + name,
                "This group holds nothing. An empty group is refused rather than read as matching everything or as matching nothing, because both readings are defensible and neither is what an operator meant to write."));
            return null;
        }

        var groups = new List<RuleConditionGroup>();
        var conditions = new List<string>();
        var index = 0;

        foreach (var member in members.EnumerateArray())
        {
            var at = string.Create(CultureInfo.InvariantCulture, $"{pointer}/{name}/{index}");
            index++;

            if (member.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new RuleValidationError(
                    at,
                    "A group holds groups and conditions, and both of those are JSON objects."));
                continue;
            }

            if (IsGroup(member))
            {
                var child = ReadGroup(member, at, depth + 1, errors);
                if (child is not null)
                {
                    groups.Add(child);
                }

                continue;
            }

            conditions.Add(at);
        }

        return new RuleConditionGroup(kind, pointer, groups, conditions);
    }

    private static bool IsGroup(JsonElement element)
    {
        foreach (var candidate in Kinds)
        {
            if (element.TryGetProperty(candidate.Name, out _))
            {
                return true;
            }
        }

        return false;
    }
}
