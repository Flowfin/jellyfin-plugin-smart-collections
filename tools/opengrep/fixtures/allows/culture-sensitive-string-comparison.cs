// The near miss for culture-sensitive-string-comparison. Fires nothing.
//
// Each line is the line opposite with the comparison named. The Invariant forms
// are accepted because they say which culture they use, which is the whole ask,
// and a rule that refused them would be refusing the repair it asks for.

internal static class NamedComparisons
{
    public static bool GenreMatches(string genre, string wanted)
    {
        return genre.ToLowerInvariant() == wanted.ToLowerInvariant();
    }

    public static bool TitleStartsWith(string title, string prefix)
    {
        return title.StartsWith("The ", StringComparison.Ordinal)
            && prefix.EndsWith("The ", StringComparison.Ordinal);
    }

    public static int Order(string left, string right)
    {
        return string.Compare(left, right, StringComparison.Ordinal);
    }
}
