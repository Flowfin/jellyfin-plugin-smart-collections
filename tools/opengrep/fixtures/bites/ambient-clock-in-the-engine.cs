// Fires ambient-clock-in-the-engine, and nothing else.
//
// A relative date resolved against a clock read here. The document says the
// same thing on both runs and the collection differs, and nothing in the result
// records which instant produced it.

internal static class AmbientClock
{
    public static bool AddedRecently(DateTimeOffset added, int days)
    {
        return added >= DateTimeOffset.UtcNow.AddDays(-days);
    }
}
