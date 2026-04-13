using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using User = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Server.Implementations.Tests.Collections;

/// <summary>
/// Tests for the Movies › Collections sub-view, which goes through
/// UserViewBuilder.GetMovieCollections() and must exclude sub-collections.
/// </summary>
public class UserViewBuilderCollectionTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static User MakeUser()
    {
        var user = new User("testuser", "auth-provider", "reset-provider");
        user.AddDefaultPermissions();
        user.AddDefaultPreferences();
        return user;
    }

    private static BoxSet MakeBoxSet(Guid id, string name, params Guid[] linkedChildIds)
    {
        var box = new BoxSet { Id = id, Name = name };
        box.LinkedChildren = linkedChildIds
            .Select(cid => new LinkedChild { LibraryItemId = cid.ToString("N"), Type = LinkedChildType.Manual })
            .ToArray();
        return box;
    }

    /// <summary>
    /// Builds a UserViewBuilder whose GetItemList returns <paramref name="allBoxSets"/>
    /// and whose GetItemsResult captures the final query for assertion.
    /// </summary>
    private static (UserViewBuilder Builder, Func<InternalItemsQuery?> GetCaptured) MakeBuilder(
        IEnumerable<BoxSet> allBoxSets)
    {
        InternalItemsQuery? captured = null;

        var libMock = new Mock<ILibraryManager>();
        libMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(allBoxSets.Cast<BaseItem>().ToList());
        libMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => captured = q)
            .Returns(new QueryResult<BaseItem>());

        var builder = new UserViewBuilder(
            new Mock<IUserViewManager>().Object,
            libMock.Object,
            new Mock<ILogger<BaseItem>>().Object,
            new Mock<IUserDataManager>().Object,
            new Mock<ITVSeriesManager>().Object);

        return (builder, () => captured);
    }

    // -------------------------------------------------------------------------
    // Movies › Collections view — GetMovieCollections
    // -------------------------------------------------------------------------

    [Fact]
    public void GetMovieCollections_ExcludesSubCollections_WhenNestedBoxSetsExist()
    {
        var rootId = Guid.NewGuid();
        var subId = Guid.NewGuid();

        var root = MakeBoxSet(rootId, "Root", subId);
        var sub = MakeBoxSet(subId, "Sub");

        var (builder, getCaptured) = MakeBuilder(new[] { root, sub });
        builder.GetUserItems(null, null, CollectionType.moviecollection, new InternalItemsQuery { User = MakeUser() });

        var captured = getCaptured();
        Assert.NotNull(captured);
        Assert.Contains(subId, captured.ExcludeItemIds);
        Assert.DoesNotContain(rootId, captured.ExcludeItemIds);
    }

    [Fact]
    public void GetMovieCollections_ExcludesAllDescendants_WhenMultiLevelNesting()
    {
        var rootId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        // root → parent → child
        var root = MakeBoxSet(rootId, "Root", parentId);
        var parent = MakeBoxSet(parentId, "Parent", childId);
        var child = MakeBoxSet(childId, "Child");

        var (builder, getCaptured) = MakeBuilder(new[] { root, parent, child });
        builder.GetUserItems(null, null, CollectionType.moviecollection, new InternalItemsQuery { User = MakeUser() });

        var captured = getCaptured();
        Assert.NotNull(captured);
        Assert.Contains(parentId, captured.ExcludeItemIds);
        Assert.Contains(childId, captured.ExcludeItemIds);
        Assert.DoesNotContain(rootId, captured.ExcludeItemIds);
    }

    [Fact]
    public void GetMovieCollections_ExcludesNothing_WhenCollectionsAreFlat()
    {
        var a = MakeBoxSet(Guid.NewGuid(), "A");
        var b = MakeBoxSet(Guid.NewGuid(), "B");

        var (builder, getCaptured) = MakeBuilder(new[] { a, b });
        builder.GetUserItems(null, null, CollectionType.moviecollection, new InternalItemsQuery { User = MakeUser() });

        var captured = getCaptured();
        Assert.NotNull(captured);
        Assert.Empty(captured.ExcludeItemIds);
    }

    [Fact]
    public void GetMovieCollections_PreservesExistingExcludeItemIds_WhenSubCollectionsExist()
    {
        var callerExcluded = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var subId = Guid.NewGuid();

        var root = MakeBoxSet(rootId, "Root", subId);
        var sub = MakeBoxSet(subId, "Sub");

        var (builder, getCaptured) = MakeBuilder(new[] { root, sub });
        builder.GetUserItems(
            null,
            null,
            CollectionType.moviecollection,
            new InternalItemsQuery { User = MakeUser(), ExcludeItemIds = [callerExcluded] });

        var captured = getCaptured();
        Assert.NotNull(captured);
        Assert.Contains(callerExcluded, captured.ExcludeItemIds);
        Assert.Contains(subId, captured.ExcludeItemIds);
        // No duplicates
        Assert.Equal(captured.ExcludeItemIds.Length, captured.ExcludeItemIds.Distinct().Count());
    }

    [Fact]
    public void GetMovieCollections_ExcludesSubCollections_WhenLinkedChildHasOnlyLibraryItemId()
    {
        var rootId = Guid.NewGuid();
        var subId = Guid.NewGuid();

        // MakeBoxSet already uses LibraryItemId exclusively (the real-world path)
        var root = MakeBoxSet(rootId, "Root", subId);
        var sub = MakeBoxSet(subId, "Sub");

        var (builder, getCaptured) = MakeBuilder(new[] { root, sub });
        builder.GetUserItems(null, null, CollectionType.moviecollection, new InternalItemsQuery { User = MakeUser() });

        var captured = getCaptured();
        Assert.NotNull(captured);
        Assert.Contains(subId, captured.ExcludeItemIds);
        Assert.DoesNotContain(rootId, captured.ExcludeItemIds);
    }

    [Fact]
    public void GetMovieCollections_DoesNotThrow_WhenCollectionsFormACycle()
    {
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        var a = MakeBoxSet(aId, "A", bId);
        var b = MakeBoxSet(bId, "B", aId);

        var (builder, _) = MakeBuilder(new[] { a, b });
        var ex = Record.Exception(() =>
            builder.GetUserItems(null, null, CollectionType.moviecollection, new InternalItemsQuery { User = MakeUser() }));

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // Path-based LinkedChild fallback (old Jellyfin data stored BoxSet–BoxSet
    // relationships by filesystem path; neither ItemId nor LibraryItemId is set)
    // -------------------------------------------------------------------------

    [Fact]
    public void GetMovieCollections_ExcludesSubCollections_WhenLinkedChildHasOnlyPath()
    {
        var rootId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        const string subPath = "/fake/path/sub";

        var sub = new BoxSet { Id = subId, Name = "Sub", Path = subPath };

        // Root links to sub via path only — no ItemId/LibraryItemId.
        var root = new BoxSet { Id = rootId, Name = "Root" };
        root.LinkedChildren = new[]
        {
            new LinkedChild { Path = subPath, Type = LinkedChildType.Manual }
        };

        var (builder, getCaptured) = MakeBuilder(new[] { root, sub });
        builder.GetUserItems(null, null, CollectionType.moviecollection, new InternalItemsQuery { User = MakeUser() });

        var captured = getCaptured();
        Assert.NotNull(captured);
        Assert.Contains(subId, captured.ExcludeItemIds);
        Assert.DoesNotContain(rootId, captured.ExcludeItemIds);
    }

    [Fact]
    public void GetMovieCollections_DoesNotThrow_WhenLinkedChildHasOnlyPath()
    {
        var rootId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        const string subPath = "/fake/path/sub";

        var sub = new BoxSet { Id = subId, Name = "Sub", Path = subPath };
        var root = new BoxSet { Id = rootId, Name = "Root" };
        root.LinkedChildren = new[]
        {
            new LinkedChild { Path = subPath, Type = LinkedChildType.Manual }
        };

        var (builder, _) = MakeBuilder(new[] { root, sub });
        var ex = Record.Exception(() =>
            builder.GetUserItems(null, null, CollectionType.moviecollection, new InternalItemsQuery { User = MakeUser() }));

        Assert.Null(ex);
    }
}
