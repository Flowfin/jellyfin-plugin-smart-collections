// The near miss for ambient-clock-in-the-engine. Fires nothing.
//
// The instant arrives as an argument, so the caller decides it and the result
// can be recorded next to it. The static member on the same type is here on
// purpose: the rule refuses reading a clock, not naming the type that reads it,
// and a rule written to match the type alone would refuse this line too.

internal static class InjectedClock
{
    public static bool AddedRecently(DateTimeOffset added, DateTimeOffset evaluatedAt, int days)
    {
        var floor = evaluatedAt.AddDays(-days);
        return floor <= DateTimeOffset.MaxValue && added >= floor;
    }
}
