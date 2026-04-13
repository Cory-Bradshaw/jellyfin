using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Api.Helpers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Dto;
using Xunit;

namespace Jellyfin.Api.Tests.Helpers;

public class CollectionPickerHelperTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static BoxSet MakeBoxSet(Guid id, string name, string? sortName = null, Guid[]? linkedChildIds = null)
    {
        var box = new BoxSet { Id = id, Name = name, SortName = sortName ?? name };
        box.LinkedChildren = (linkedChildIds ?? Array.Empty<Guid>())
            .Select(cid => new LinkedChild { LibraryItemId = cid.ToString("N"), Type = LinkedChildType.Manual })
            .ToArray();
        return box;
    }

    private static BaseItemDto MakeDto(Guid id, string name, string? sortName = null)
        => new BaseItemDto { Id = id, Name = name, SortName = sortName ?? name };

    // -------------------------------------------------------------------------
    // No-op cases
    // -------------------------------------------------------------------------

    [Fact]
    public void AnnotateAndSort_ReturnsUnchanged_WhenNoSubCollections()
    {
        var id = Guid.NewGuid();
        var dtos = new[] { MakeDto(id, "My Collection") };
        var result = CollectionPickerHelper.AnnotateAndSort(dtos, Array.Empty<Guid>(), new List<BoxSet>());

        Assert.Single(result);
        Assert.Equal("My Collection", result[0].Name);
    }

    [Fact]
    public void AnnotateAndSort_DoesNotModifyRootCollections()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var root = MakeBoxSet(rootId, "Root", linkedChildIds: new[] { childId });
        var child = MakeBoxSet(childId, "Child");

        var result = CollectionPickerHelper.AnnotateAndSort(
            new[] { MakeDto(rootId, "Root"), MakeDto(childId, "Child") },
            new HashSet<Guid> { childId },
            new List<BoxSet> { root, child });

        var resultRoot = result.First(d => d.Id.Equals(rootId));
        Assert.Equal("Root", resultRoot.Name);
        Assert.DoesNotContain("\u2192", resultRoot.Name, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Arrow prefix
    // -------------------------------------------------------------------------

    [Fact]
    public void AnnotateAndSort_PrefixesSubCollectionNameWithArrow()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var root = MakeBoxSet(rootId, "Root", linkedChildIds: new[] { childId });
        var child = MakeBoxSet(childId, "Child");

        var result = CollectionPickerHelper.AnnotateAndSort(
            new[] { MakeDto(rootId, "Root"), MakeDto(childId, "Child") },
            new HashSet<Guid> { childId },
            new List<BoxSet> { root, child });

        var annotated = result.First(d => d.Id.Equals(childId));
        Assert.StartsWith("\u2192 ", annotated.Name, StringComparison.Ordinal);
        Assert.Contains("Child", annotated.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void AnnotateAndSort_MultiLevel_PrefixesWithArrow()
    {
        var grandId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var grand = MakeBoxSet(grandId, "Grand", linkedChildIds: new[] { parentId });
        var parent = MakeBoxSet(parentId, "Parent", linkedChildIds: new[] { childId });
        var child = MakeBoxSet(childId, "Child");

        var result = CollectionPickerHelper.AnnotateAndSort(
            new[] { MakeDto(grandId, "Grand"), MakeDto(parentId, "Parent"), MakeDto(childId, "Child") },
            new HashSet<Guid> { parentId, childId },
            new List<BoxSet> { grand, parent, child });

        var childAnnotated = result.First(d => d.Id.Equals(childId));
        // Depth 2 (child of parent which is child of grand): "-- → Child"
        Assert.StartsWith("-- \u2192 ", childAnnotated.Name, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Sort order
    // -------------------------------------------------------------------------

    [Fact]
    public void AnnotateAndSort_SubCollectionSortNamePutsItAfterParent()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var root = MakeBoxSet(rootId, "A Root", sortName: "A Root", linkedChildIds: new[] { childId });
        var child = MakeBoxSet(childId, "B Child", sortName: "B Child");

        var result = CollectionPickerHelper.AnnotateAndSort(
            new[] { MakeDto(rootId, "A Root", "A Root"), MakeDto(childId, "B Child", "B Child") },
            new HashSet<Guid> { childId },
            new List<BoxSet> { root, child });

        var rootIndex = Array.FindIndex(result, d => d.Id.Equals(rootId));
        var childIndex = Array.FindIndex(result, d => d.Id.Equals(childId));

        Assert.True(rootIndex < childIndex, "Sub-collection must appear after its parent");
    }

    [Fact]
    public void AnnotateAndSort_SubCollectionSortNameContainsParentPrefix()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var root = MakeBoxSet(rootId, "Jurassic Park Collection", sortName: "Jurassic Park Collection", linkedChildIds: new[] { childId });
        var child = MakeBoxSet(childId, "Jurassic Park", sortName: "Jurassic Park");

        var result = CollectionPickerHelper.AnnotateAndSort(
            new[] { MakeDto(rootId, "Jurassic Park Collection", "Jurassic Park Collection"), MakeDto(childId, "Jurassic Park", "Jurassic Park") },
            new HashSet<Guid> { childId },
            new List<BoxSet> { root, child });

        var childDto = result.First(d => d.Id.Equals(childId));
        Assert.Contains("Jurassic Park Collection", childDto.SortName, StringComparison.Ordinal);
    }

    [Fact]
    public void AnnotateAndSort_MultipleSubCollections_AllSortUnderParent()
    {
        var rootId = Guid.NewGuid();
        var sub1Id = Guid.NewGuid();
        var sub2Id = Guid.NewGuid();
        var otherRootId = Guid.NewGuid();

        var root = MakeBoxSet(rootId, "A Root", linkedChildIds: new[] { sub1Id, sub2Id });
        var sub1 = MakeBoxSet(sub1Id, "A Sub 1");
        var sub2 = MakeBoxSet(sub2Id, "A Sub 2");
        var otherRoot = MakeBoxSet(otherRootId, "Z Other Root");

        var result = CollectionPickerHelper.AnnotateAndSort(
            new[]
            {
                MakeDto(rootId, "A Root", "A Root"),
                MakeDto(sub1Id, "A Sub 1", "A Sub 1"),
                MakeDto(sub2Id, "A Sub 2", "A Sub 2"),
                MakeDto(otherRootId, "Z Other Root", "Z Other Root")
            },
            new HashSet<Guid> { sub1Id, sub2Id },
            new List<BoxSet> { root, sub1, sub2, otherRoot });

        var rootIndex = Array.FindIndex(result, d => d.Id.Equals(rootId));
        var sub1Index = Array.FindIndex(result, d => d.Id.Equals(sub1Id));
        var sub2Index = Array.FindIndex(result, d => d.Id.Equals(sub2Id));
        var otherIndex = Array.FindIndex(result, d => d.Id.Equals(otherRootId));

        Assert.True(rootIndex < sub1Index, "Sub 1 must appear after root");
        Assert.True(rootIndex < sub2Index, "Sub 2 must appear after root");
        Assert.True(sub1Index < otherIndex, "Sub 1 must appear before other root");
        Assert.True(sub2Index < otherIndex, "Sub 2 must appear before other root");
    }

    // -------------------------------------------------------------------------
    // Depth prefix (dash indentation)
    // -------------------------------------------------------------------------

    [Fact]
    public void AnnotateAndSort_DepthOne_HasNoLeadingDash()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var root = MakeBoxSet(rootId, "Root", linkedChildIds: new[] { childId });
        var child = MakeBoxSet(childId, "Child");

        var result = CollectionPickerHelper.AnnotateAndSort(
            new[] { MakeDto(rootId, "Root"), MakeDto(childId, "Child") },
            new HashSet<Guid> { childId },
            new List<BoxSet> { root, child });

        var annotated = result.First(d => d.Id.Equals(childId));
        // Depth 1: "→ Child" — no leading dash
        Assert.StartsWith("\u2192 ", annotated.Name, StringComparison.Ordinal);
        Assert.False(annotated.Name.StartsWith('-'), "Depth-1 name must not start with a dash");
    }

    [Fact]
    public void AnnotateAndSort_DepthTwo_HasOneDash()
    {
        var grandId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var grand = MakeBoxSet(grandId, "Grand", linkedChildIds: new[] { parentId });
        var parent = MakeBoxSet(parentId, "Parent", linkedChildIds: new[] { childId });
        var child = MakeBoxSet(childId, "Child");

        var result = CollectionPickerHelper.AnnotateAndSort(
            new[] { MakeDto(grandId, "Grand"), MakeDto(parentId, "Parent"), MakeDto(childId, "Child") },
            new HashSet<Guid> { parentId, childId },
            new List<BoxSet> { grand, parent, child });

        var grandChild = result.First(d => d.Id.Equals(childId));
        // Depth 2: "-- → Child"
        Assert.StartsWith("-- \u2192 ", grandChild.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void AnnotateAndSort_DepthThree_HasTwoDashes()
    {
        var greatGrandId = Guid.NewGuid();
        var grandId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var greatGrand = MakeBoxSet(greatGrandId, "Great Grand", linkedChildIds: new[] { grandId });
        var grand = MakeBoxSet(grandId, "Grand", linkedChildIds: new[] { parentId });
        var parent = MakeBoxSet(parentId, "Parent", linkedChildIds: new[] { childId });
        var child = MakeBoxSet(childId, "Child");

        var result = CollectionPickerHelper.AnnotateAndSort(
            new[]
            {
                MakeDto(greatGrandId, "Great Grand"),
                MakeDto(grandId, "Grand"),
                MakeDto(parentId, "Parent"),
                MakeDto(childId, "Child")
            },
            new HashSet<Guid> { grandId, parentId, childId },
            new List<BoxSet> { greatGrand, grand, parent, child });

        var deepChild = result.First(d => d.Id.Equals(childId));
        // Depth 3: "---- → Child"
        Assert.StartsWith("---- \u2192 ", deepChild.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void AnnotateAndSort_MultiLevel_SortsGrandChildAfterParent()
    {
        var grandId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var otherRootId = Guid.NewGuid();

        var grand = MakeBoxSet(grandId, "A Grand", sortName: "A Grand", linkedChildIds: new[] { parentId });
        var parent = MakeBoxSet(parentId, "A Parent", sortName: "A Parent", linkedChildIds: new[] { childId });
        var child = MakeBoxSet(childId, "A Child", sortName: "A Child");
        var otherRoot = MakeBoxSet(otherRootId, "Z Other", sortName: "Z Other");

        var result = CollectionPickerHelper.AnnotateAndSort(
            new[]
            {
                MakeDto(grandId, "A Grand", "A Grand"),
                MakeDto(parentId, "A Parent", "A Parent"),
                MakeDto(childId, "A Child", "A Child"),
                MakeDto(otherRootId, "Z Other", "Z Other")
            },
            new HashSet<Guid> { parentId, childId },
            new List<BoxSet> { grand, parent, child, otherRoot });

        var grandIndex = Array.FindIndex(result, d => d.Id.Equals(grandId));
        var parentIndex = Array.FindIndex(result, d => d.Id.Equals(parentId));
        var childIndex = Array.FindIndex(result, d => d.Id.Equals(childId));
        var otherIndex = Array.FindIndex(result, d => d.Id.Equals(otherRootId));

        Assert.True(grandIndex < parentIndex, "Parent must appear after grand");
        Assert.True(parentIndex < childIndex, "Child must appear after parent");
        Assert.True(childIndex < otherIndex, "Other root must appear last");
    }

    [Fact]
    public void AnnotateAndSort_DepthOne_ParentIsNotPrefixedWithDash()
    {
        // Verify the parent (depth 1) gets "→ " and its child (depth 2) gets "- → "
        // — the parent must not also gain a dash just because it has a child.
        var rootId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var root = MakeBoxSet(rootId, "Root", linkedChildIds: new[] { parentId });
        var parent = MakeBoxSet(parentId, "Parent", linkedChildIds: new[] { childId });
        var child = MakeBoxSet(childId, "Child");

        var result = CollectionPickerHelper.AnnotateAndSort(
            new[] { MakeDto(rootId, "Root"), MakeDto(parentId, "Parent"), MakeDto(childId, "Child") },
            new HashSet<Guid> { parentId, childId },
            new List<BoxSet> { root, parent, child });

        var parentDto = result.First(d => d.Id.Equals(parentId));
        Assert.StartsWith("\u2192 ", parentDto.Name, StringComparison.Ordinal);
        Assert.False(parentDto.Name.StartsWith('-'), "Depth-1 name must not start with a dash");
    }

    // -------------------------------------------------------------------------
    // Cycle detection — circular collection references must not cause infinite recursion
    // -------------------------------------------------------------------------

    [Fact]
    public void AnnotateAndSort_DoesNotThrow_WhenCollectionsFormACycle()
    {
        // A → B → A (mutual reference). Both are "sub-collections" of each other.
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        var a = MakeBoxSet(aId, "A", linkedChildIds: new[] { bId });
        var b = MakeBoxSet(bId, "B", linkedChildIds: new[] { aId });

        var ex = Record.Exception(() => CollectionPickerHelper.AnnotateAndSort(
            new[] { MakeDto(aId, "A"), MakeDto(bId, "B") },
            new HashSet<Guid> { aId, bId },
            new List<BoxSet> { a, b }));

        Assert.Null(ex);
    }

    [Fact]
    public void AnnotateAndSort_ReturnsBothEntries_WhenCollectionsFormACycle()
    {
        // Even with a cycle both DTOs must appear in the result exactly once.
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        var a = MakeBoxSet(aId, "A", linkedChildIds: new[] { bId });
        var b = MakeBoxSet(bId, "B", linkedChildIds: new[] { aId });

        var result = CollectionPickerHelper.AnnotateAndSort(
            new[] { MakeDto(aId, "A"), MakeDto(bId, "B") },
            new HashSet<Guid> { aId, bId },
            new List<BoxSet> { a, b });

        Assert.Equal(2, result.Length);
        Assert.Single(result, d => d.Id.Equals(aId));
        Assert.Single(result, d => d.Id.Equals(bId));
    }

    [Fact]
    public void AnnotateAndSort_PrefixesWithArrow_WhenCollectionsFormACycle()
    {
        // With a cycle A→B→A, the parent chain walk stops at the cycle boundary.
        // Each entry is still annotated with "→ " (depth 1) since neither has a
        // resolvable root ancestor outside the cycle.
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        var a = MakeBoxSet(aId, "A", linkedChildIds: new[] { bId });
        var b = MakeBoxSet(bId, "B", linkedChildIds: new[] { aId });

        var result = CollectionPickerHelper.AnnotateAndSort(
            new[] { MakeDto(aId, "A"), MakeDto(bId, "B") },
            new HashSet<Guid> { aId, bId },
            new List<BoxSet> { a, b });

        foreach (var d in result)
        {
            Assert.StartsWith("\u2192 ", d.Name, StringComparison.Ordinal);
        }
    }
}
