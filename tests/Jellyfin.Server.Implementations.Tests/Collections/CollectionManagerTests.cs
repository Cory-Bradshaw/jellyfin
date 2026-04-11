using System;
using System.Collections.Generic;
using System.Linq;
using Emby.Server.Implementations.Collections;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using User = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Server.Implementations.Tests.Collections;

public class CollectionManagerTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static BoxSet MakeBoxSet(Guid id, string name, params Guid[] linkedChildIds)
    {
        var box = new BoxSet { Id = id, Name = name };
        box.LinkedChildren = linkedChildIds
            .Select(cid => new LinkedChild { LibraryItemId = cid.ToString("N"), Type = LinkedChildType.Manual })
            .ToArray();
        return box;
    }

    private static Movie MakeMovie(Guid id, string name)
        => new Movie { Id = id, Name = name };

    /// <summary>
    /// Builds a <see cref="CollectionManager"/> whose <c>GetItemList</c> always returns
    /// <paramref name="allBoxSets"/> regardless of the query filter.
    /// </summary>
    private static CollectionManager MakeManager(IEnumerable<BoxSet> allBoxSets)
    {
        var libMock = new Mock<ILibraryManager>();
        libMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(allBoxSets.Cast<BaseItem>().ToList());

        return new CollectionManager(
            libMock.Object,
            new Mock<IApplicationPaths>().Object,
            new Mock<ILocalizationManager>().Object,
            new Mock<IFileSystem>().Object,
            new Mock<ILibraryMonitor>().Object,
            new Mock<ILoggerFactory>().Object,
            new Mock<IProviderManager>().Object);
    }

    // -------------------------------------------------------------------------
    // GetSubCollectionIds
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSubCollectionIds_ReturnsEmpty_WhenNoBoxSets()
    {
        var manager = MakeManager(Enumerable.Empty<BoxSet>());
        Assert.Empty(manager.GetSubCollectionIds());
    }

    [Fact]
    public void GetSubCollectionIds_ReturnsEmpty_WhenCollectionsAreFlat()
    {
        var a = MakeBoxSet(Guid.NewGuid(), "A");
        var b = MakeBoxSet(Guid.NewGuid(), "B");
        var manager = MakeManager(new[] { a, b });

        Assert.Empty(manager.GetSubCollectionIds());
    }

    [Fact]
    public void GetSubCollectionIds_ReturnsChild_WhenDirectlyNested()
    {
        var childId = Guid.NewGuid();
        var parent = MakeBoxSet(Guid.NewGuid(), "Parent", childId);
        var child = MakeBoxSet(childId, "Child");
        var manager = MakeManager(new[] { parent, child });

        var subIds = manager.GetSubCollectionIds();
        Assert.Contains(childId, subIds);
        Assert.DoesNotContain(parent.Id, subIds);
    }

    [Fact]
    public void GetSubCollectionIds_ReturnsAllDescendants_WhenMultiLevel()
    {
        var grandId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var grand = MakeBoxSet(grandId, "Grand", parentId);
        var parent = MakeBoxSet(parentId, "Parent", childId);
        var child = MakeBoxSet(childId, "Child");
        var manager = MakeManager(new[] { grand, parent, child });

        var subIds = manager.GetSubCollectionIds();
        Assert.Contains(parentId, subIds);
        Assert.Contains(childId, subIds);
        Assert.DoesNotContain(grandId, subIds);
    }

    private static User MakeUser()
    {
        var user = new User("testuser", "auth-provider", "reset-provider");
        user.AddDefaultPermissions();
        user.AddDefaultPreferences();
        return user;
    }

    // -------------------------------------------------------------------------
    // CollapseItemsWithinBoxSets — Movies view rendering
    // -------------------------------------------------------------------------

    [Fact]
    public void CollapseItemsWithinBoxSets_ReplacesMovieWithRootCollection_WhenMovieInFlatCollection()
    {
        var collectionId = Guid.NewGuid();
        var movieId = Guid.NewGuid();

        var collection = MakeBoxSet(collectionId, "JP Collection", movieId);
        var movie = MakeMovie(movieId, "Jurassic Park");

        var manager = MakeManager(new[] { collection });
        var result = manager.CollapseItemsWithinBoxSets(new BaseItem[] { movie }, MakeUser()).ToList();

        Assert.Single(result);
        Assert.Equal(collectionId, result[0].Id);
    }

    [Fact]
    public void CollapseItemsWithinBoxSets_LeavesMovieVisible_WhenMovieNotInAnyCollection()
    {
        var collection = MakeBoxSet(Guid.NewGuid(), "Empty Collection");
        var movie = MakeMovie(Guid.NewGuid(), "Unrelated Movie");

        var manager = MakeManager(new[] { collection });
        var result = manager.CollapseItemsWithinBoxSets(new BaseItem[] { movie }, MakeUser()).ToList();

        Assert.Single(result);
        Assert.Equal(movie.Id, result[0].Id);
    }

    [Fact]
    public void CollapseItemsWithinBoxSets_HidesMovieInSubCollection_ShowsOnlyRootCollection()
    {
        // Root → Sub → Movie
        var rootId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var movieId = Guid.NewGuid();

        var root = MakeBoxSet(rootId, "Root", subId);
        var sub = MakeBoxSet(subId, "Sub", movieId);
        var movie = MakeMovie(movieId, "The Movie");

        var manager = MakeManager(new[] { root, sub });
        var result = manager.CollapseItemsWithinBoxSets(new BaseItem[] { movie }, MakeUser()).ToList();

        Assert.Single(result);
        Assert.Equal(rootId, result[0].Id);
    }

    [Fact]
    public void CollapseItemsWithinBoxSets_SubCollectionIsNotInResults_WhenMovieIsInSubCollection()
    {
        var rootId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var movieId = Guid.NewGuid();

        var root = MakeBoxSet(rootId, "Root", subId);
        var sub = MakeBoxSet(subId, "Sub", movieId);
        var movie = MakeMovie(movieId, "The Movie");

        var manager = MakeManager(new[] { root, sub });
        var result = manager.CollapseItemsWithinBoxSets(new BaseItem[] { movie }, MakeUser()).ToList();

        Assert.DoesNotContain(result, r => r.Id.Equals(subId));
    }

    [Fact]
    public void CollapseItemsWithinBoxSets_MoviesInBothRootAndSubCollapse_ToSameRoot()
    {
        var rootId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var movie1Id = Guid.NewGuid();
        var movie2Id = Guid.NewGuid();

        // Root has movie1 directly and sub-collection; sub has movie2.
        var root = MakeBoxSet(rootId, "Root", movie1Id, subId);
        var sub = MakeBoxSet(subId, "Sub", movie2Id);
        var movie1 = MakeMovie(movie1Id, "Movie 1");
        var movie2 = MakeMovie(movie2Id, "Movie 2");

        var manager = MakeManager(new[] { root, sub });
        var result = manager.CollapseItemsWithinBoxSets(new BaseItem[] { movie1, movie2 }, MakeUser()).ToList();

        // Both movies collapse into the single root collection.
        Assert.Single(result);
        Assert.Equal(rootId, result[0].Id);
    }

    // -------------------------------------------------------------------------
    // Cycle detection — circular collection references must not cause infinite recursion
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSubCollectionIds_DoesNotThrow_WhenCollectionsFormACycle()
    {
        // A → B → A (mutual reference)
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        var a = MakeBoxSet(aId, "A", bId);
        var b = MakeBoxSet(bId, "B", aId);
        var manager = MakeManager(new[] { a, b });

        // Must complete without StackOverflowException. Both are sub-collections of each other.
        var subIds = manager.GetSubCollectionIds();
        Assert.Contains(aId, subIds);
        Assert.Contains(bId, subIds);
    }

    [Fact]
    public void CollapseItemsWithinBoxSets_DoesNotThrow_WhenCollectionsFormACycle()
    {
        // A → B → A (mutual reference), movie is in B
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        var movieId = Guid.NewGuid();

        var a = MakeBoxSet(aId, "A", bId);
        var b = MakeBoxSet(bId, "B", movieId);
        var movie = MakeMovie(movieId, "The Movie");

        var manager = MakeManager(new[] { a, b });

        // Must complete without StackOverflowException.
        var ex = Record.Exception(() => manager.CollapseItemsWithinBoxSets(new BaseItem[] { movie }, MakeUser()).ToList());
        Assert.Null(ex);
    }

    [Fact]
    public void CollapseItemsWithinBoxSets_MovieVisibleOnce_WhenCollectionsFormACycle()
    {
        // A → B → A, movie in B. A is the root (nothing points to A except B which is a sub).
        // Since A→B and B→A, both are sub-collections of each other — GetSubCollectionIds returns both.
        // Therefore neither collapses movies cleanly; the movie itself should appear (legacy path).
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        var movieId = Guid.NewGuid();

        var a = MakeBoxSet(aId, "A", bId);
        var b = MakeBoxSet(bId, "B", movieId);
        var movie = MakeMovie(movieId, "The Movie");

        var manager = MakeManager(new[] { a, b });
        var result = manager.CollapseItemsWithinBoxSets(new BaseItem[] { movie }, MakeUser()).ToList();

        // Result must contain exactly one entry — no duplicates from cycle traversal.
        Assert.Single(result);
    }
}
