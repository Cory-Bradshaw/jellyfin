using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Extensions;
using Jellyfin.Api.ModelBinders;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Collections;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// The collection controller.
/// </summary>
[Route("Collections")]
[Authorize(Policy = Policies.CollectionManagement)]
public class CollectionController : BaseJellyfinApiController
{
    private readonly ICollectionManager _collectionManager;
    private readonly IDtoService _dtoService;
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionController"/> class.
    /// </summary>
    /// <param name="collectionManager">Instance of <see cref="ICollectionManager"/> interface.</param>
    /// <param name="dtoService">Instance of <see cref="IDtoService"/> interface.</param>
    /// <param name="libraryManager">Instance of <see cref="ILibraryManager"/> interface.</param>
    /// <param name="providerManager">Instance of <see cref="IProviderManager"/> interface.</param>
    public CollectionController(
        ICollectionManager collectionManager,
        IDtoService dtoService,
        ILibraryManager libraryManager,
        IProviderManager providerManager)
    {
        _collectionManager = collectionManager;
        _dtoService = dtoService;
        _libraryManager = libraryManager;
        _providerManager = providerManager;
    }

    /// <summary>
    /// Creates a new collection.
    /// </summary>
    /// <param name="name">The name of the collection.</param>
    /// <param name="ids">Item Ids to add to the collection.</param>
    /// <param name="parentId">Optional. Create the collection within a specific folder.</param>
    /// <param name="isLocked">Whether or not to lock the new collection.</param>
    /// <response code="200">Collection created.</response>
    /// <returns>A <see cref="CollectionCreationOptions"/> with information about the new collection.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CollectionCreationResult>> CreateCollection(
        [FromQuery] string? name,
        [FromQuery, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] string[] ids,
        [FromQuery] Guid? parentId,
        [FromQuery] bool isLocked = false)
    {
        var userId = User.GetUserId();

        var item = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
        {
            IsLocked = isLocked,
            Name = name,
            ParentId = parentId,
            ItemIdList = ids,
            UserIds = new[] { userId }
        }).ConfigureAwait(false);

        var dtoOptions = new DtoOptions();

        var dto = _dtoService.GetBaseItemDto(item, dtoOptions);

        return new CollectionCreationResult
        {
            Id = dto.Id
        };
    }

    /// <summary>
    /// Adds items to a collection.
    /// </summary>
    /// <param name="collectionId">The collection id.</param>
    /// <param name="ids">Item ids, comma delimited.</param>
    /// <response code="204">Items added to collection.</response>
    /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
    [HttpPost("{collectionId}/Items")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> AddToCollection(
        [FromRoute, Required] Guid collectionId,
        [FromQuery, Required, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] Guid[] ids)
    {
        await _collectionManager.AddToCollectionAsync(collectionId, ids).ConfigureAwait(true);
        return NoContent();
    }

    /// <summary>
    /// Removes items from a collection.
    /// </summary>
    /// <param name="collectionId">The collection id.</param>
    /// <param name="ids">Item ids, comma delimited.</param>
    /// <response code="204">Items removed from collection.</response>
    /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
    [HttpDelete("{collectionId}/Items")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> RemoveFromCollection(
        [FromRoute, Required] Guid collectionId,
        [FromQuery, Required, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] Guid[] ids)
    {
        await _collectionManager.RemoveFromCollectionAsync(collectionId, ids).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Copies a backdrop image from the collection to the primary image slot.
    /// Used to let users select which generated layout variant to use as the primary.
    /// </summary>
    /// <param name="collectionId">The collection id.</param>
    /// <param name="backdropIndex">Zero-based index of the backdrop to promote.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="204">Primary image updated.</response>
    /// <response code="404">Collection or backdrop not found.</response>
    /// <returns>A <see cref="NoContentResult"/> on success.</returns>
    [HttpPost("{collectionId}/Images/Backdrop/{backdropIndex}/SetAsPrimary")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetBackdropAsPrimary(
        [FromRoute, Required] Guid collectionId,
        [FromRoute, Required] int backdropIndex,
        CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById<BoxSet>(collectionId, User.GetUserId());
        if (item is null)
        {
            return NotFound();
        }

        var backdropImage = item.GetImageInfo(ImageType.Backdrop, backdropIndex);
        if (backdropImage is null || !backdropImage.IsLocalFile || !System.IO.File.Exists(backdropImage.Path))
        {
            return NotFound();
        }

        var stream = System.IO.File.OpenRead(backdropImage.Path);
        await using (stream.ConfigureAwait(false))
        {
            await _providerManager.SaveImage(item, stream, "image/png", ImageType.Primary, null, cancellationToken)
                .ConfigureAwait(false);
        }

        await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken)
            .ConfigureAwait(false);

        return NoContent();
    }
}
