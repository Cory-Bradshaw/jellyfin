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

    /// <summary>
    /// Creates a BoxSet whose first linked child uses only <c>Path</c> (no ItemId/LibraryItemId),
    /// simulating old Jellyfin data that stored BoxSet-to-BoxSet relationships by filesystem path.
    /// </summary>
    private static (BoxSet Parent, BoxSet Child) MakeBoxSetsWithPathOnlyLink(
        Guid parentId, string parentName, Guid childId, string childName, string path = "/fake/path/child")
    {
        var child = new BoxSet { Id = childId, Name = childName, Path = path };
        var parent = new BoxSet { Id = parentId, Name = parentName };
        parent.LinkedChildren = new[]
        {
            new LinkedChild { Path = path, Type = LinkedChildType.Manual }
            // Note: ItemId and LibraryItemId are intentionally NOT set.
        };
        return (parent, child);
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

        var loggerMock = new Mock<ILogger<CollectionManager>>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(false);
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(loggerMock.Object);

        return new CollectionManager(
            libMock.Object,
            new Mock<IApplicationPaths>().Object,
            new Mock<ILocalizationManager>().Object,
            new Mock<IFileSystem>().Object,
            new Mock<ILibraryMonitor>().Object,
            loggerFactoryMock.Object,
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
    // Movies view — movies in sub-collections must not appear as individual items
    // -------------------------------------------------------------------------

    [Fact]
    public void CollapseItemsWithinBoxSets_MovieInSubCollection_DoesNotAppearAsIndividualItem()
    {
        // Root → Sub → Movie. The movie must collapse to Root, not appear standalone.
        var rootId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var movieId = Guid.NewGuid();

        var root = MakeBoxSet(rootId, "Root", subId);
        var sub = MakeBoxSet(subId, "Sub", movieId);
        var movie = MakeMovie(movieId, "The Movie");

        var manager = MakeManager(new[] { root, sub });
        var result = manager.CollapseItemsWithinBoxSets(new BaseItem[] { movie }, MakeUser()).ToList();

        Assert.DoesNotContain(result, r => r.Id.Equals(movieId));
    }

    [Fact]
    public void CollapseItemsWithinBoxSets_MovieInSubCollection_CollapseToRoot()
    {
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
    public void CollapseItemsWithinBoxSets_MovieInDeepSubCollection_CollapseToRoot()
    {
        // Root → Sub → SubSub → Movie (three levels deep)
        var rootId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var subSubId = Guid.NewGuid();
        var movieId = Guid.NewGuid();

        var root = MakeBoxSet(rootId, "Root", subId);
        var sub = MakeBoxSet(subId, "Sub", subSubId);
        var subSub = MakeBoxSet(subSubId, "SubSub", movieId);
        var movie = MakeMovie(movieId, "Deep Movie");

        var manager = MakeManager(new[] { root, sub, subSub });
        var result = manager.CollapseItemsWithinBoxSets(new BaseItem[] { movie }, MakeUser()).ToList();

        Assert.Single(result);
        Assert.Equal(rootId, result[0].Id);
        Assert.DoesNotContain(result, r => r.Id.Equals(movieId));
    }

    [Fact]
    public void CollapseItemsWithinBoxSets_MoviesAcrossMultipleSubCollections_AllCollapseToRespectiveRoots()
    {
        // RootA → SubA → Movie1
        // RootB → SubB → Movie2
        var rootAId = Guid.NewGuid();
        var subAId = Guid.NewGuid();
        var movie1Id = Guid.NewGuid();
        var rootBId = Guid.NewGuid();
        var subBId = Guid.NewGuid();
        var movie2Id = Guid.NewGuid();

        var rootA = MakeBoxSet(rootAId, "Root A", subAId);
        var subA = MakeBoxSet(subAId, "Sub A", movie1Id);
        var rootB = MakeBoxSet(rootBId, "Root B", subBId);
        var subB = MakeBoxSet(subBId, "Sub B", movie2Id);
        var movie1 = MakeMovie(movie1Id, "Movie 1");
        var movie2 = MakeMovie(movie2Id, "Movie 2");

        var manager = MakeManager(new[] { rootA, subA, rootB, subB });
        var result = manager.CollapseItemsWithinBoxSets(new BaseItem[] { movie1, movie2 }, MakeUser()).ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Id.Equals(rootAId));
        Assert.Contains(result, r => r.Id.Equals(rootBId));
        Assert.DoesNotContain(result, r => r.Id.Equals(movie1Id));
        Assert.DoesNotContain(result, r => r.Id.Equals(movie2Id));
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

    // -------------------------------------------------------------------------
    // Path-based LinkedChild fallback (old Jellyfin data stored BoxSet–BoxSet
    // relationships by filesystem path; neither ItemId nor LibraryItemId is set)
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSubCollectionIds_DetectsSubCollection_WhenLinkedChildHasOnlyPath()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var (parent, child) = MakeBoxSetsWithPathOnlyLink(parentId, "Parent", childId, "Child");

        var manager = MakeManager(new[] { parent, child });
        var subIds = manager.GetSubCollectionIds();

        Assert.Contains(childId, subIds);
        Assert.DoesNotContain(parentId, subIds);
    }

    [Fact]
    public void GetSubCollectionIds_DoesNotThrow_WhenLinkedChildHasOnlyPath()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var (parent, child) = MakeBoxSetsWithPathOnlyLink(parentId, "Parent", childId, "Child");

        var manager = MakeManager(new[] { parent, child });
        var ex = Record.Exception(() => manager.GetSubCollectionIds());

        Assert.Null(ex);
    }

    [Fact]
    public void GetSubCollectionIds_IgnoresPathChild_WhenPathDoesNotMatchAnyBoxSet()
    {
        var parentId = Guid.NewGuid();
        var parent = new BoxSet { Id = parentId, Name = "Parent" };
        parent.LinkedChildren = new[]
        {
            new LinkedChild { Path = "/no/such/path", Type = LinkedChildType.Manual }
        };

        var manager = MakeManager(new[] { parent });
        var subIds = manager.GetSubCollectionIds();

        Assert.Empty(subIds);
    }

    [Fact]
    public void CollapseItemsWithinBoxSets_MovieInSubCollection_CollapseToRoot_WhenLinkedChildHasOnlyPath()
    {
        // Root → Sub (path-only link) → Movie (LibraryItemId link)
        var rootId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        const string subPath = "/fake/path/sub";

        // Sub contains the movie via normal LibraryItemId link.
        var sub = MakeBoxSet(subId, "Sub", movieId);
        sub.Path = subPath;

        // Root links to Sub using path only — no ItemId/LibraryItemId.
        var root = new BoxSet { Id = rootId, Name = "Root" };
        root.LinkedChildren = new[]
        {
            new LinkedChild { Path = subPath, Type = LinkedChildType.Manual }
        };

        var movie = MakeMovie(movieId, "The Movie");

        var manager = MakeManager(new[] { root, sub });
        var result = manager.CollapseItemsWithinBoxSets(new BaseItem[] { movie }, MakeUser()).ToList();

        Assert.Single(result);
        Assert.Equal(rootId, result[0].Id);
        Assert.DoesNotContain(result, r => r.Id.Equals(movieId));
    }

    [Fact]
    public void CollapseItemsWithinBoxSets_DoesNotThrow_WhenLinkedChildHasOnlyPath()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        const string childPath = "/fake/path/child";

        var child = MakeBoxSet(childId, "Child", movieId);
        child.Path = childPath;

        var root = new BoxSet { Id = rootId, Name = "Root" };
        root.LinkedChildren = new[]
        {
            new LinkedChild { Path = childPath, Type = LinkedChildType.Manual }
        };

        var movie = MakeMovie(movieId, "The Movie");

        var manager = MakeManager(new[] { root, child });
        var ex = Record.Exception(() =>
            manager.CollapseItemsWithinBoxSets(new BaseItem[] { movie }, MakeUser()).ToList());

        Assert.Null(ex);
    }
}
