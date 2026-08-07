// Fires culture-sensitive-string-comparison, and nothing else.
//
// Four shapes, because a fixture that reaches only the first of them proves
// only the first of them. The last is the capital spelling of the type, which
// the rule read past until this fixture was written.
//
// On a Turkish server the first line stops matching a genre written with a
// capital I, and nothing about the rule document changed.

internal static class CultureSensitiveMatching
{
    public static bool GenreMatches(string genre, string wanted)
    {
        return genre.ToLower() == wanted.ToLower();
    }

    public static bool TitleStartsWith(string title, string prefix)
    {
        return title.StartsWith("The ") && prefix.EndsWith("The ");
    }

    public static int Order(string left, string right)
    {
        return string.Compare(left, right);
    }

    public static int OrderTrimmed(string left, string right)
    {
        return String.Compare(left.Trim(), right);
    }
}
