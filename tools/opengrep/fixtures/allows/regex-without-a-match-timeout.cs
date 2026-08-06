// The near miss for regex-without-a-match-timeout. Fires nothing.
//
// One argument away from the fixture next door. A reader comparing the two sees
// the whole rule, and a rule that fired on this one would be refusing every
// regular expression rather than the unbounded ones.

internal static class RegexWithAMatchTimeout
{
    public static bool Matches(string pattern, string value)
    {
        var compiled = new Regex(
            pattern,
            RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100));
        return compiled.IsMatch(value);
    }
}
