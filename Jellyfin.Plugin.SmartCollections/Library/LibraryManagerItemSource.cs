using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SmartCollections.Evaluation;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.SmartCollections.Library;

/// <summary>
/// The server's library manager, seen through the one question an evaluation asks it.
/// </summary>
/// <remarks>
/// One forward and no state, which is what <see cref="LibraryManagerChangeSource"/> is for the
/// three events this plugin subscribes to. The query is handed on exactly as the compiler narrowed
/// it: nothing here adds a property, because a rule narrowed anywhere but in
/// <c>RuleQueryCompiler</c> is a rule narrowed where no document can be read.
///
/// Nothing in the suite executes this method, because the only way to reach it is to hold a real
/// <see cref="ILibraryManager"/>, which means a running server. What the suite covers instead is
/// the type on the other side of the boundary, <c>RuleEvaluator</c>, against a stand-in for
/// <see cref="IRuleItemSource"/>. The residual is one forwarding line that a reader can check by
/// eye and no test can reach, and it is the same residual this plugin already carries for the
/// change source beside it.
/// </remarks>
public sealed class LibraryManagerItemSource : IRuleItemSource
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryManagerItemSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The server's library manager.</param>
    /// <exception cref="ArgumentNullException"><paramref name="libraryManager"/> is <see langword="null"/>.</exception>
    public LibraryManagerItemSource(ILibraryManager libraryManager)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public IReadOnlyList<BaseItem> Select(InternalItemsQuery query) => _libraryManager.GetItemList(query);
}
