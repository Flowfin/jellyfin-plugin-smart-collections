// The near miss for web-framework-type-in-the-engine. Fires nothing.
//
// The stage takes an argument and answers with a value of its own, so a test
// hands it an identifier and reads the answer with nothing running. Whoever
// received the request turned it into that argument, and whoever answers it
// turns this value into a response.
//
// The last three members are the near misses the rule has to survive. Generic
// patterns read comments and string literals as well as code, so a rule matching
// a bare route or a bare authorisation would refuse the sentence you are reading
// and the two identifiers below: a type whose name begins with a refused token
// and continues into another word is a different type, and the word route is
// English before it is an attribute.

internal sealed class RefreshEngine
{
    public RefreshOutcome Refresh(string ruleId)
    {
        return new RefreshOutcome(ruleId);
    }

    public bool WouldRoute(RouteKind kind)
    {
        return kind == RouteKind.Direct;
    }

    public HttpGetter Getter { get; init; }

    public ControllerBaseline Baseline { get; init; }
}
