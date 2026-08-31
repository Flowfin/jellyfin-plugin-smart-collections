using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// What every rule's last few refreshes did, kept so a rule that has been failing is readable as
/// one rather than as a collection that looks stale.
/// </summary>
/// <remarks>
/// An applier answers a caller and forgets. A collection that stopped updating is then a fault
/// nobody can see without the server's log, and one bad run and a rule that has failed every run
/// since it was written look identical from outside: both are a collection whose contents are
/// wrong. Keeping the last few outcomes is what separates them.
///
/// Keyed on the rule and never on the collection. A collection an operator deletes comes back
/// under the same mark with a new identifier, which
/// <c>CollectionResolverTests.ARuleWhoseCollectionWasDeletedComesBackUnderTheSameMark</c> asserts,
/// so a table keyed on <see cref="CollectionRefreshOutcome.CollectionId"/> starts a fresh history
/// at exactly the moment an operator was trying to repair something - the run before the deletion
/// and the run after it are two runs of one rule, and the failures that led to the deletion are
/// what the operator needs to still be there.
///
/// The depth is a bound rather than a policy. Every run of every rule kept for the life of the
/// server is unbounded growth on a server nobody restarts, and the question this record answers -
/// is this rule failing, or did it fail once - is answered by the last few. It is a constructor
/// argument with a recorded default rather than a constant, so the surface that reads this can
/// decide it without this type reading a configuration it has no business knowing about.
///
/// Nothing here reads a clock, and that is a limit rather than an oversight. The instant a run
/// used is the instant its rule resolved relative dates against; it is an input to an evaluation
/// and is recorded with its result, which is what <c>ambient-clock-in-the-engine</c> refuses the
/// alternative to. Nothing evaluates a rule yet, so no outcome carries one, and this history can
/// say that the last four runs failed and cannot say over how many days. Reading it as "failing
/// for a week" is reading more than it holds.
///
/// The table is guarded because a refresh run walks several collections and a server may drive
/// more than one run at once, so entries arrive from more than one thread. The lock is held only
/// across the list operations, never across a caller's work, which is the difference between this
/// and <see cref="CollectionRefreshGate"/>: that one excludes writes to a server, this one
/// protects a dictionary.
/// </remarks>
public sealed class CollectionRefreshHistory
{
    /// <summary>
    /// How many runs per rule are kept where a caller names no depth.
    /// </summary>
    /// <remarks>
    /// Ten, which is a week of daily refreshes with room for the manual ones an operator makes
    /// while repairing a rule. The number is not measured against anything - no refresh has ever
    /// run on a server - so it is a defensible starting bound rather than a figure, and it is
    /// written here so that changing it is a change to one line with this sentence beside it.
    /// </remarks>
    public const int DefaultDepth = 10;

    private readonly Dictionary<string, List<CollectionRefreshOutcome>> _perRule =
        new(StringComparer.Ordinal);

    private readonly object _sync = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionRefreshHistory"/> class keeping
    /// <see cref="DefaultDepth"/> runs per rule.
    /// </summary>
    public CollectionRefreshHistory()
        : this(DefaultDepth)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionRefreshHistory"/> class.
    /// </summary>
    /// <param name="depth">How many runs to keep per rule.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="depth"/> is below one. A history keeping nothing is not a shorter history,
    /// it is a caller that meant to keep the last result and got a record that answers nothing,
    /// and it would read as a rule that has never run.
    /// </exception>
    public CollectionRefreshHistory(int depth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);

        Depth = depth;
    }

    /// <summary>
    /// Gets how many runs are kept per rule.
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// Gets the rules that have run at least once, in ascending ordinal order.
    /// </summary>
    /// <remarks>
    /// Sorted rather than handed out in whatever order the table enumerates in. A page rendering
    /// this twice with nothing changed in between has to produce the same list both times, and a
    /// dictionary's order is an accident of how it was filled.
    /// </remarks>
    public IReadOnlyList<string> Rules
    {
        get
        {
            lock (_sync)
            {
                var rules = new List<string>(_perRule.Keys);
                rules.Sort(StringComparer.Ordinal);
                return rules;
            }
        }
    }

    /// <summary>
    /// Keeps what one refresh did to one collection.
    /// </summary>
    /// <param name="outcome">What the refresh did.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outcome"/> is <see langword="null"/>.</exception>
    public void Record(CollectionRefreshOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        lock (_sync)
        {
            if (!_perRule.TryGetValue(outcome.RuleId, out var runs))
            {
                runs = new List<CollectionRefreshOutcome>(Depth);
                _perRule.Add(outcome.RuleId, runs);
            }

            // Newest first, so the last result is the head and reading down is reading back in
            // time. The list is at most Depth long, so the insert is over a handful of entries.
            runs.Insert(0, outcome);
            if (runs.Count > Depth)
            {
                runs.RemoveAt(runs.Count - 1);
            }
        }
    }

    /// <summary>
    /// Keeps what one run did, which is one outcome per collection it covered.
    /// </summary>
    /// <param name="outcomes">What the run did, as the applier reported it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outcomes"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Each outcome goes under its own rule. A run covering several rules is several rules' runs
    /// and never one, which is the same reason the applier answers one outcome per collection
    /// instead of one verdict for the run.
    /// </remarks>
    public void Record(IReadOnlyList<CollectionRefreshOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        foreach (var outcome in outcomes)
        {
            Record(outcome);
        }
    }

    /// <summary>
    /// What this rule's most recent refresh did.
    /// </summary>
    /// <param name="ruleId">The rule's identity, as its document declares it.</param>
    /// <returns>The most recent outcome, or <see langword="null"/> where the rule has never run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ruleId"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A rule that has never run answers null rather than throwing. A rule document an operator
    /// added a minute ago is exactly that, and it is the ordinary state of a page rendering a
    /// directory rather than a caller's mistake.
    ///
    /// The head is read without asking whether the list is empty, because an entry is only ever
    /// created by the insert that fills it and nothing removes the last one. A guard there would
    /// be a branch no input can reach, which reads as care and is a line nothing proves.
    /// </remarks>
    public CollectionRefreshOutcome? Last(string ruleId)
    {
        ArgumentNullException.ThrowIfNull(ruleId);

        lock (_sync)
        {
            return _perRule.TryGetValue(ruleId, out var runs) ? runs[0] : null;
        }
    }

    /// <summary>
    /// What this rule's last few refreshes did, newest first.
    /// </summary>
    /// <param name="ruleId">The rule's identity, as its document declares it.</param>
    /// <returns>
    /// At most <see cref="Depth"/> outcomes, newest first, and empty where the rule has never run.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="ruleId"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A copy rather than the list itself. A caller reading a rule's history while a refresh
    /// records into it would otherwise be enumerating a list another thread is inserting into, and
    /// the page that reads this is the surface most likely to do exactly that.
    /// </remarks>
    public IReadOnlyList<CollectionRefreshOutcome> For(string ruleId)
    {
        ArgumentNullException.ThrowIfNull(ruleId);

        lock (_sync)
        {
            return _perRule.TryGetValue(ruleId, out var runs)
                ? new List<CollectionRefreshOutcome>(runs)
                : [];
        }
    }
}
