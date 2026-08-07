// The near miss for network-client-in-the-engine. Fires nothing.
//
// Everything the engine needs from outside arrives as an injected interface,
// and the interface is one this repository declares rather than one that opens
// a socket.
//
// The last line is the near miss the rule has to survive: a name that begins
// with a refused token and continues into a different word is a different type,
// and a rule matching the prefix would refuse it.

internal sealed class QueryingEngine
{
    private readonly ILibraryQuery _library;

    public QueryingEngine(ILibraryQuery library)
    {
        _library = library;
    }

    public static bool Retryable(System.Net.HttpStatusCode status)
    {
        return status == System.Net.HttpStatusCode.ServiceUnavailable;
    }
}
