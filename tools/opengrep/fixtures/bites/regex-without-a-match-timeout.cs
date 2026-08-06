// Fires regex-without-a-match-timeout, and nothing else.
//
// This is the construction the leading plugin in this space uses: a pattern
// taken from the operator's document, compiled, and handed no bound on how long
// a match may run.
//
// Nothing compiles this file. It sits outside both project directories, so no
// csproj globs it, and the only thing that reads it is the lint.

internal static class RegexWithoutATimeout
{
    public static bool Matches(string pattern, string value)
    {
        var compiled = new Regex(pattern, RegexOptions.Compiled);
        return compiled.IsMatch(value);
    }
}
