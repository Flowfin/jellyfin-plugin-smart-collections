// Fires network-client-in-the-engine, and nothing else.
//
// An engine that fetches something is an engine whose answer depends on a
// server it does not own being up, and on what that server said today. It also
// ends the claim that no test here needs a machine trust store, because a call
// that goes out needs one.

internal sealed class EnrichingEngine
{
    private readonly HttpClient _client;

    public EnrichingEngine(HttpClient client)
    {
        _client = client;
    }
}
