// Fires regex-without-a-match-timeout, and nothing else.
//
// Two spellings, because the rule has to catch both. The first is the
// construction the leading plugin in this space uses: a pattern taken from the
// operator's document, compiled, and handed no bound on how long a match may
// run. The second is the target-typed form, where the type sits on the field
// and the construction says only `new(`, which is how every regular expression
// in this repository is written today.
//
// Nothing compiles this file. It sits outside both project directories, so no
// csproj globs it, and the only thing that reads it is the lint.

internal static class RegexWithoutATimeout
{
    private static readonly Regex Spelled = new(
        "^(a+)+$",
        RegexOptions.Compiled);

    public static bool Matches(string pattern, string value)
    {
        var compiled = new Regex(pattern, RegexOptions.Compiled);
        return compiled.IsMatch(value) && Spelled.IsMatch(value);
    }
}
