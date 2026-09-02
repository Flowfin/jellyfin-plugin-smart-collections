// Fires web-framework-type-in-the-engine, and nothing else.
//
// An engine stage that answers with an action result can only be exercised
// through a request pipeline. A test wanting to know what the stage decided has
// to build a request, a context and a route, none of which the decision depends
// on, and the decision itself becomes unreachable from a value.

internal sealed class RefreshEngine : ControllerBase
{
    [HttpPost("Refresh")]
    public IActionResult Refresh([FromBody] string ruleId)
    {
        return Accepted(ruleId);
    }
}
