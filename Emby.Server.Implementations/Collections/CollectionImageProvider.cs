using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.Images;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;

namespace Emby.Server.Implementations.Collections
{
    /// <summary>
    /// A collection image provider.
    /// </summary>
    public class CollectionImageProvider : BaseDynamicImageProvider<BoxSet>, ICustomMetadataProvider<BoxSet>, IDynamicImageProvider, IHasItemChangeMonitor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CollectionImageProvider"/> class.
        /// </summary>
        /// <param name="fileSystem">The filesystem.</param>
        /// <param name="providerManager">The provider manager.</param>
        /// <param name="applicationPaths">The application paths.</param>
        /// <param name="imageProcessor">The image processor.</param>
        public CollectionImageProvider(
            IFileSystem fileSystem,
            IProviderManager providerManager,
            IApplicationPaths applicationPaths,
            IImageProcessor imageProcessor)
            : base(fileSystem, providerManager, applicationPaths, imageProcessor)
        {
        }

        /// <inheritdoc />
        bool IImageProvider.Supports(BaseItem item) => item is BoxSet;

        /// <inheritdoc />
        IEnumerable<ImageType> IDynamicImageProvider.GetSupportedImages(BaseItem item)
            => [ImageType.Primary];

        /// <summary>
        /// Called during image-only refresh (e.g. right-click → Refresh Images).
        /// Generates one fresh waterfall composite and returns the temp-file path so the
        /// caller can save it into the item's metadata folder.
        /// </summary>
        /// <param name="item">The item to generate an image for.</param>
        /// <param name="type">The image type requested.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="DynamicImageResponse"/> with the generated image.</returns>
        async Task<DynamicImageResponse> IDynamicImageProvider.GetImage(
            BaseItem item,
            ImageType type,
            CancellationToken cancellationToken)
        {
            if (item is not BoxSet boxSet || item.IsLocked || type != ImageType.Primary)
            {
                return new DynamicImageResponse { HasImage = false };
            }

            var covers = GetItemsWithImages(boxSet);
            if (covers.Count == 0)
            {
                return new DynamicImageResponse { HasImage = false };
            }

            var shuffled = covers.OrderBy(_ => Random.Shared.Next()).ToList();
            // Use the default layout for this collection's size (first entry in the layout list).
            var defaultLayout = GetLayoutsForCollectionSize(shuffled.Count)[0];
            var tempPath = await GenerateCollageToPathAsync(boxSet, shuffled, defaultLayout, cancellationToken)
                .ConfigureAwait(false);

            if (tempPath is null)
            {
                return new DynamicImageResponse { HasImage = false };
            }

            return new DynamicImageResponse
            {
                HasImage = true,
                Path = tempPath,
                Protocol = MediaBrowser.Model.MediaInfo.MediaProtocol.File,
                Format = ImageFormat.Png
            };
        }

        /// <inheritdoc />
        /// <remarks>
        /// Overrides the base-class implementation (which only checks Primary) so that the
        /// provider is also triggered when any expected Backdrop variant is missing — e.g. on
        /// a collection that was generated before multi-layout support was added.
        /// </remarks>
        bool IHasItemChangeMonitor.HasChanged(BaseItem item, IDirectoryService directoryService)
        {
            if (item is not BoxSet boxSet || !Supports(item))
            {
                return false;
            }

            var allCovers = GetItemsWithImages(boxSet);
            if (allCovers.Count == 0)
            {
                return false;
            }

            var layouts = GetLayoutsForCollectionSize(allCovers.Count);

            for (int i = 0; i < layouts.Length; i++)
            {
                var imageType = i == 0 ? ImageType.Primary : ImageType.Backdrop;
                int imageIndex = i == 0 ? 0 : i - 1;
                var image = item.GetImageInfo(imageType, imageIndex);

                if (image is null)
                {
                    // Slot is missing — needs to be generated.
                    return true;
                }

                if (!image.IsLocalFile)
                {
                    // Remote (internet-fetched) image in this slot — not ours to manage.
                    continue;
                }

                if (!FileSystem.ContainsSubPath(item.GetInternalMetadataPath(), image.Path))
                {
                    // User-assigned image outside the metadata folder — preserve it, don't flag as changed.
                    continue;
                }

                // Our generated composite — check whether the collection has changed since it was written.
                if (HasChangedByDate(item, image))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Generates any missing or stale layout variants for the collection.
        /// Each slot (Primary and Backdrop alternatives) is evaluated independently:
        /// user-assigned images outside the metadata folder are never touched; slots that
        /// are already up-to-date are skipped; missing or stale slots are (re)generated.
        /// </summary>
        /// <param name="item">The collection item.</param>
        /// <param name="options">Metadata refresh options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Flags indicating which image types were updated.</returns>
        async Task<ItemUpdateType> ICustomMetadataProvider<BoxSet>.FetchAsync(
            BoxSet item,
            MetadataRefreshOptions options,
            CancellationToken cancellationToken)
        {
            if (item.IsLocked)
            {
                return ItemUpdateType.None;
            }

            var allCovers = GetItemsWithImages(item);
            if (allCovers.Count == 0)
            {
                return ItemUpdateType.None;
            }

            var layouts = GetLayoutsForCollectionSize(allCovers.Count);
            var updateType = ItemUpdateType.None;

            for (int i = 0; i < layouts.Length; i++)
            {
                var imageType = i == 0 ? ImageType.Primary : ImageType.Backdrop;
                int imageIndex = i == 0 ? 0 : i - 1;
                int? nullableIndex = i == 0 ? null : imageIndex;
                var existing = item.GetImageInfo(imageType, imageIndex);

                // Never overwrite an image the user explicitly assigned from outside the metadata folder.
                if (existing is not null
                    && existing.IsLocalFile
                    && !FileSystem.ContainsSubPath(item.GetInternalMetadataPath(), existing.Path))
                {
                    continue;
                }

                // Skip a slot that already has an up-to-date generated image
                // (unless a full image refresh was explicitly requested — e.g. by the
                // "Generate" button in the image editor — in which case regenerate regardless).
                if (options.ImageRefreshMode < MetadataRefreshMode.FullRefresh
                    && existing is not null
                    && !HasChangedByDate(item, existing))
                {
                    continue;
                }

                // Slot is either missing or stale — generate it.
                updateType |= await SaveLayoutVariantAsync(item, allCovers, layouts[i], imageType, nullableIndex, cancellationToken)
                    .ConfigureAwait(false);
            }

            return updateType;
        }

        /// <summary>
        /// Returns the ordered list of collage layouts to generate for a collection of the given size.
        /// The first entry is the default style (saved as Primary); the rest become Backdrop alternatives.
        /// </summary>
        private static CollageType[] GetLayoutsForCollectionSize(int count) => count switch
        {
            // 1 image: even split degrades gracefully to a single full-canvas poster.
            1 => [CollageType.EvenSplit],

            // 2–3 movies: small-collection layouts, best-first.
            2 or 3 => [CollageType.EvenSplit, CollageType.DiagonalCut, CollageType.HeroAndStrip],

            // 4–8 movies: quad grid as the clean default; waterfall and hero strip as alternatives.
            <= 8 => [CollageType.QuadGrid, CollageType.Waterfall, CollageType.HeroAndStrip],

            // 9+ movies: waterfall shows the breadth; quad and hero strip as tighter alternatives.
            _ => [CollageType.Waterfall, CollageType.QuadGrid, CollageType.HeroAndStrip]
        };

        private async Task<ItemUpdateType> SaveLayoutVariantAsync(
            BaseItem item,
            IReadOnlyList<BaseItem> items,
            CollageType layout,
            ImageType imageType,
            int? imageIndex,
            CancellationToken cancellationToken)
        {
            var tempPath = await GenerateCollageToPathAsync(item, items, layout, cancellationToken)
                .ConfigureAwait(false);

            if (tempPath is null)
            {
                return ItemUpdateType.None;
            }

            await ProviderManager.SaveImage(
                item,
                tempPath,
                MediaTypeNames.Image.Png,
                imageType,
                imageIndex,
                false,
                cancellationToken).ConfigureAwait(false);

            return ItemUpdateType.ImageUpdate;
        }

        /// <summary>
        /// Generates a composite image using <paramref name="layout"/> from <paramref name="items"/>
        /// into a temp file.
        /// </summary>
        /// <param name="item">The parent collection (used for collage title).</param>
        /// <param name="items">The leaf items whose covers are composed.</param>
        /// <param name="layout">The collage layout to use.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Path to the generated PNG, or <see langword="null"/> if generation failed.</returns>
        private Task<string?> GenerateCollageToPathAsync(
            BaseItem item,
            IReadOnlyList<BaseItem> items,
            CollageType layout,
            CancellationToken cancellationToken)
        {
            var imagePaths = GetStripCollageImagePaths(item, items).ToArray();
            if (imagePaths.Length == 0 || !ImageProcessor.SupportsImageCollageCreation)
            {
                return Task.FromResult<string?>(null);
            }

            var tempPath = Path.Combine(
                ApplicationPaths.TempDirectory,
                Guid.NewGuid().ToString("N") + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

            ImageProcessor.CreateImageCollage(
                new ImageCollageOptions
                {
                    // 4:3 landscape composite — matches the double-wide card's aspect ratio.
                    Width = 800,
                    Height = 600,
                    OutputPath = tempPath,
                    InputPaths = imagePaths,
                    CollageType = layout
                },
                item.Name);

            return Task.FromResult<string?>(File.Exists(tempPath) ? tempPath : null);
        }

        /// <inheritdoc />
        /// <remarks>
        /// The base-class implementation checks whether the image file itself changed on disk,
        /// which never happens for generated composites (we write them once and they stay).
        /// Instead, treat the composite as stale whenever any leaf-level item in the collection
        /// was modified more recently than the image — this covers movies being added, removed,
        /// or re-scanned without requiring the user to manually delete images first.
        /// </remarks>
        protected override bool HasChangedByDate(BaseItem item, ItemImageInfo image)
        {
            return GetLeafItems((BoxSet)item, [])
                .Any(child => child.DateModified > image.DateModified);
        }

        /// <summary>
        /// Recursively collects leaf-level items (movies, episodes, etc.) from the BoxSet
        /// hierarchy, skipping sub-collection composites entirely.
        /// </summary>
        /// <param name="item">The collection item.</param>
        /// <returns>Leaf items that have a primary image.</returns>
        protected override IReadOnlyList<BaseItem> GetItemsWithImages(BaseItem item)
        {
            return GetLeafItems((BoxSet)item, [])
                .Where(i => i.HasImage(ImageType.Primary))
                .DistinctBy(i => i.Id)
                .OrderBy(i => i.SortName)
                .ToList();
        }

        private static IEnumerable<BaseItem> GetLeafItems(BoxSet boxSet, HashSet<Guid> visited)
        {
            if (!visited.Add(boxSet.Id))
            {
                yield break;
            }

            // Combine folder children (legacy box sets) and linked children (modern collections).
            var children = boxSet.Children.Concat(boxSet.GetLinkedChildren());
            foreach (var child in children)
            {
                if (child is BoxSet nested)
                {
                    foreach (var leaf in GetLeafItems(nested, visited))
                    {
                        yield return leaf;
                    }
                }
                else
                {
                    yield return child;
                }
            }
        }

        // CreateImage is not called in the normal flow for this provider (FetchAsync is handled
        // via explicit interface implementation above), but the base class requires an override.

        /// <inheritdoc />
        protected override string CreateImage(
            BaseItem item,
            IReadOnlyCollection<BaseItem> itemsWithImages,
            string outputPathWithoutExtension,
            ImageType imageType,
            int imageIndex) => null!;
    }
}
