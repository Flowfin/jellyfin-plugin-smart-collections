using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A stand-in for one of the server's own manager interfaces: the members a test names are
/// answered and recorded, and every other member throws.
/// </summary>
/// <remarks>
/// <c>docs/testing.md</c> refuses a test that boots a server, and the reason the two collection
/// adapters had no test until #263 was written into this tree in those words: an
/// <c>ILibraryManager</c> is eighty-four members, so a hand-written stand-in for it is eighty-one
/// method bodies nobody reads in order to reach three that matter. That is a fact about how a fake
/// is WRITTEN rather than about whether one can exist, and this is the other way of writing it.
///
/// <para>
/// <see cref="DispatchProxy"/> is in the base class library, so this costs no package and no
/// generator. It builds a type implementing the interface at run time and sends every call here,
/// which means the stand-in is exactly as large as the part of the server a test actually names.
/// </para>
///
/// <para>
/// AN UNNAMED MEMBER THROWS, AND THAT IS THE ASSERTION RATHER THAN A CONVENIENCE. A hand-written
/// fake whose unused members throw proves that the code under test touched nothing else; this
/// keeps that property and drops the typing. It also survives the difference between the two
/// server lines, which a hand-written one would not: <c>ICollectionManager</c> carries a member on
/// 12.0 that 10.11 does not, so a checked-in class implementing it would need a conditional
/// compilation block for a member no test here names.
/// </para>
///
/// <para>
/// WHAT IT CANNOT DO is answer for anything but an interface, and it holds no type checking of the
/// answers: an answer of the wrong type reaches the caller as an invalid cast rather than as a
/// compiler error. Both are the price of the reflection, and the second is bounded by the answers
/// being written three lines from the assertion that reads them.
/// </para>
/// </remarks>
public class FakeServer : DispatchProxy
{
    private Dictionary<string, Func<object?[], object?>> _answers = [];
    private List<string> _calls = [];
    private string _named = string.Empty;

    /// <summary>
    /// Gets each member that was called, in order, by name.
    /// </summary>
    public IReadOnlyList<string> Calls => _calls;

    /// <summary>
    /// Builds a stand-in for a server interface.
    /// </summary>
    /// <typeparam name="T">The server interface to stand in for.</typeparam>
    /// <param name="answers">
    /// What each named member answers, by member name. A member absent from this throws when it is
    /// called, naming itself.
    /// </param>
    /// <returns>The stand-in, and the recorder behind it.</returns>
    public static (T Server, FakeServer Recorder) For<T>(Dictionary<string, Func<object?[], object?>> answers)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(answers);

        var proxy = Create<T, FakeServer>();
        var recorder = (FakeServer)(object)proxy;

        recorder._answers = answers;
        recorder._calls = [];
        recorder._named = typeof(T).Name;

        return (proxy, recorder);
    }

    /// <summary>
    /// Answers one call, or refuses it for being a member no test named.
    /// </summary>
    /// <param name="targetMethod">The member that was called.</param>
    /// <param name="args">What it was called with.</param>
    /// <returns>What the named answer produced.</returns>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var name = targetMethod.Name;
        _calls.Add(name);

        if (!_answers.TryGetValue(name, out var answer))
        {
            throw new NotSupportedException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{_named}.{name} was called, and no test named it. The members this stand-in answers are: {string.Join(", ", _answers.Keys.OrderBy(key => key, StringComparer.Ordinal))}."));
        }

        return answer(args ?? []);
    }
}
