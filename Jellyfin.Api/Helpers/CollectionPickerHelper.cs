using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Api.Helpers;

/// <summary>
/// Helpers for the collection picker UI — annotation and sorting of BoxSet DTOs.
/// </summary>
public static class CollectionPickerHelper
{
    /// <summary>
    /// Annotates sub-collection DTOs with a "→ " display-name prefix and a sort name
    /// that places each sub-collection directly after its parent in alphabetical order.
    /// Root collection DTOs are not modified. The returned array is sorted by SortName.
    /// </summary>
    /// <remarks>
    /// Only the DTO's <c>Name</c> and <c>SortName</c> fields are mutated — the stored
    /// item name is never changed, so adding a movie to a decorated entry still works
    /// correctly.
    /// </remarks>
    /// <param name="dtos">DTOs to annotate (modified in place).</param>
    /// <param name="subCollectionIds">IDs that are sub-collections of another BoxSet.</param>
    /// <param name="allBoxSets">All BoxSets in the library (needed to resolve parent chains).</param>
    /// <returns>The same array, re-sorted so sub-collections follow their parent.</returns>
    public static BaseItemDto[] AnnotateAndSort(
        BaseItemDto[] dtos,
        IReadOnlyCollection<Guid> subCollectionIds,
        IReadOnlyList<BoxSet> allBoxSets)
    {
        if (subCollectionIds.Count == 0)
        {
            return dtos;
        }

        // Build child id → parent BoxSet lookup.
        var childToParent = new Dictionary<Guid, BoxSet>();
        foreach (var bs in allBoxSets)
        {
            foreach (var lc in bs.LinkedChildren)
            {
                Guid? lcId = lc.ItemId is { } lcItemId && !lcItemId.Equals(Guid.Empty)
                    ? lcItemId
                    : !string.IsNullOrEmpty(lc.LibraryItemId)
                        && Guid.TryParse(lc.LibraryItemId, out var parsed) ? parsed : (Guid?)null;

                if (lcId.HasValue && subCollectionIds.Contains(lcId.Value))
                {
                    childToParent.TryAdd(lcId.Value, bs);
                }
            }
        }

        // Capture original DTO sort names before any mutation so ancestor sort prefixes
        // are derived from the same keys used to sort root collections, not from the
        // BoxSet domain-object SortName which may differ or be null.
        var originalSortNames = dtos.ToDictionary(d => d.Id, d => d.SortName ?? d.Name ?? string.Empty);

        foreach (var dto in dtos)
        {
            if (!subCollectionIds.Contains(dto.Id))
            {
                continue;
            }

            // Walk up the parent chain to build a fully-qualified sort prefix.
            // The visited set guards against circular references (A→B→A), stopping
            // at the first repeated node and treating it as the effective root.
            var prefixParts = new List<string>();
            var current = dto.Id;
            var visited = new HashSet<Guid> { current };
            while (childToParent.TryGetValue(current, out var parentBs) && visited.Add(parentBs.Id))
            {
                // Prefer the DTO-layer sort name so the prefix sorts identically to the
                // parent row itself; fall back to the domain-object name if the parent
                // is not in the DTO list (e.g. a grandparent not included in results).
                var parentSortName = originalSortNames.TryGetValue(parentBs.Id, out var s)
                    ? s
                    : parentBs.SortName ?? parentBs.Name ?? string.Empty;
                prefixParts.Insert(0, parentSortName);
                current = parentBs.Id;
            }

            // depth 1 → "→ Name", depth 2 → "-- → Name", depth 3 → "---- → Name", etc.
            var depth = prefixParts.Count;
            var depthPrefix = depth > 1 ? new string('-', (depth - 1) * 2) + " " : string.Empty;
            var ancestorSortPrefix = string.Join(" \u00bb ", prefixParts);
            // Compute SortName before mutating Name so the trailing key segment never
            // contains "→" or "- " (which sort after letters, breaking grouping).
            var originalName = dto.Name;
            dto.SortName = ancestorSortPrefix + " \u00bb " + (dto.SortName ?? originalName);
            dto.Name = depthPrefix + "\u2192 " + originalName;
        }

        return dtos
            .OrderBy(d => d.SortName ?? d.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
