using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Jellyfin.Drawing.Skia;

/// <summary>
/// Used to build collages of multiple images arranged in vertical strips.
/// </summary>
public partial class StripCollageBuilder
{
    private readonly SkiaEncoder _skiaEncoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="StripCollageBuilder"/> class.
    /// </summary>
    /// <param name="skiaEncoder">The encoder to use for building collages.</param>
    public StripCollageBuilder(SkiaEncoder skiaEncoder)
    {
        _skiaEncoder = skiaEncoder;
    }

    [GeneratedRegex(@"\p{IsArabic}|\p{IsArmenian}|\p{IsHebrew}|\p{IsSyriac}|\p{IsThaana}")]
    private static partial Regex IsRtlTextRegex();

    /// <summary>
    /// Check which format an image has been encoded with using its filename extension.
    /// </summary>
    /// <param name="outputPath">The path to the image to get the format for.</param>
    /// <returns>The image format.</returns>
    public static SKEncodedImageFormat GetEncodedFormat(string outputPath)
    {
        ArgumentNullException.ThrowIfNull(outputPath);

        var ext = Path.GetExtension(outputPath.AsSpan());

        if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return SKEncodedImageFormat.Jpeg;
        }

        if (ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            return SKEncodedImageFormat.Webp;
        }

        if (ext.Equals(".gif", StringComparison.OrdinalIgnoreCase))
        {
            return SKEncodedImageFormat.Gif;
        }

        if (ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            return SKEncodedImageFormat.Bmp;
        }

        // default to png
        return SKEncodedImageFormat.Png;
    }

    /// <summary>
    /// Create a square collage.
    /// </summary>
    /// <param name="paths">The paths of the images to use in the collage.</param>
    /// <param name="outputPath">The path at which to place the resulting collage image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildSquareCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildSquareCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    /// <summary>
    /// Create a thumb collage.
    /// </summary>
    /// <param name="paths">The paths of the images to use in the collage.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    /// <param name="libraryName">The name of the library to draw on the collage.</param>
    public void BuildThumbCollage(IReadOnlyList<string> paths, string outputPath, int width, int height, string? libraryName)
    {
        using var bitmap = BuildThumbCollageBitmap(paths, width, height, libraryName);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildThumbCollageBitmap(IReadOnlyList<string> paths, int width, int height, string? libraryName)
    {
        var bitmap = new SKBitmap(width, height);

        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        using var backdrop = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, 0, out _);
        if (backdrop is null)
        {
            return bitmap;
        }

        // resize to the same aspect as the original
        var backdropHeight = Math.Abs(width * backdrop.Height / backdrop.Width);
        using var resizedBackdrop = SkiaEncoder.ResizeImage(backdrop, new SKImageInfo(width, backdropHeight, backdrop.ColorType, backdrop.AlphaType, backdrop.ColorSpace));
        using var paint = new SKPaint();
        // draw the backdrop
        canvas.DrawImage(resizedBackdrop, 0, 0, SkiaEncoder.DefaultSamplingOptions, paint);

        // draw shadow rectangle
        using var paintColor = new SKPaint();
        paintColor.Color = SKColors.Black.WithAlpha(0x78);
        paintColor.Style = SKPaintStyle.Fill;
        canvas.DrawRect(0, 0, width, height, paintColor);

        var typeFace = SkiaEncoder.DefaultTypeFace;

        // draw library name
        using var textFont = new SKFont();
        textFont.Size = 112;
        textFont.Typeface = typeFace;
        using var textPaint = new SKPaint();
        textPaint.Color = SKColors.White;
        textPaint.Style = SKPaintStyle.Fill;
        textPaint.IsAntialias = true;

        // scale down text to 90% of the width if text is larger than 95% of the width
        var textWidth = textFont.MeasureText(libraryName);
        if (textWidth > width * 0.95)
        {
            textFont.Size = 0.9f * width * textFont.Size / textWidth;
        }

        if (string.IsNullOrWhiteSpace(libraryName))
        {
            return bitmap;
        }

        var realWidth = DrawText(null, 0, (height / 2f) + (textFont.Metrics.XHeight / 2), libraryName, textPaint, textFont);
        if (realWidth > width * 0.95)
        {
            textFont.Size = 0.9f * width * textFont.Size / realWidth;
            realWidth = DrawText(null, 0, (height / 2f) + (textFont.Metrics.XHeight / 2), libraryName, textPaint, textFont);
        }

        var padding = (width - realWidth) / 2;

        if (IsRtlTextRegex().IsMatch(libraryName))
        {
            DrawText(canvas, width - padding, (height / 2f) + (textFont.Metrics.XHeight / 2), libraryName, textPaint, textFont, true);
        }
        else
        {
            DrawText(canvas, padding, (height / 2f) + (textFont.Metrics.XHeight / 2), libraryName, textPaint, textFont);
        }

        return bitmap;
    }

    private SKBitmap BuildSquareCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        var imageIndex = 0;
        var cellWidth = width / 2;
        var cellHeight = height / 2;

        using var canvas = new SKCanvas(bitmap);
        for (var x = 0; x < 2; x++)
        {
            for (var y = 0; y < 2; y++)
            {
                using var currentBitmap = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, imageIndex, out int newIndex);
                imageIndex = newIndex;

                if (currentBitmap is null)
                {
                    continue;
                }

                // Scale image
                var imageInfo = new SKImageInfo(cellWidth, cellHeight, currentBitmap.ColorType, currentBitmap.AlphaType, currentBitmap.ColorSpace);
                using var resizeImage = SkiaEncoder.ResizeImage(currentBitmap, imageInfo);
                using var paint = new SKPaint();

                // draw this image into the strip at the next position
                var xPos = x * cellWidth;
                var yPos = y * cellHeight;
                canvas.DrawImage(resizeImage, xPos, yPos, SkiaEncoder.DefaultSamplingOptions, paint);
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Center-crops <paramref name="src"/> to fill <paramref name="dstRect"/> exactly (cover, not contain).
    /// </summary>
    private static void DrawCenterCropped(SKCanvas canvas, SKBitmap src, SKRect dstRect)
    {
        float srcAspect = (float)src.Width / src.Height;
        float dstAspect = dstRect.Width / dstRect.Height;

        SKRect srcRect;
        if (srcAspect > dstAspect)
        {
            float cropWidth = src.Height * dstAspect;
            float cropX = (src.Width - cropWidth) / 2f;
            srcRect = new SKRect(cropX, 0, cropX + cropWidth, src.Height);
        }
        else
        {
            float cropHeight = src.Width / dstAspect;
            float cropY = (src.Height - cropHeight) / 2f;
            srcRect = new SKRect(0, cropY, src.Width, cropY + cropHeight);
        }

        using var paint = new SKPaint();
        canvas.DrawBitmap(src, srcRect, dstRect, SkiaEncoder.DefaultSamplingOptions, paint);
    }

    /// <summary>
    /// Create an even-split collage: 2 equal vertical columns for 2-image sets, 3 for 3-image sets.
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildEvenSplitCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildEvenSplitCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildEvenSplitCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (paths.Count == 0)
        {
            return bitmap;
        }

        // Cap at 3 columns; if only 1 path, fill the whole canvas.
        int numCols = Math.Min(paths.Count, 3);
        const int separator = 2;
        float colWidth = (width - (separator * (numCols - 1))) / (float)numCols;

        for (int col = 0; col < numCols; col++)
        {
            float x = col * (colWidth + separator);
            var dstRect = new SKRect(x, 0, x + colWidth, height);

            using var img = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, col % paths.Count, out _);
            if (img is not null)
            {
                DrawCenterCropped(canvas, img, dstRect);
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Create a diagonal-cut collage: a diagonal slash divides the canvas between two posters.
    /// </summary>
    /// <param name="paths">The paths of the images to use (first two are used).</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildDiagonalCutCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildDiagonalCutCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildDiagonalCutCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (paths.Count == 0)
        {
            return bitmap;
        }

        // The slash is a diagonal line through the center. slashLean controls how far the top
        // and bottom endpoints deviate from the vertical midpoint (18% of height each way).
        float centerX = width * 0.5f;
        float slashLean = height * 0.18f;

        // Left region: top-left → (centerX + lean, 0) → (centerX − lean, height) → bottom-left
        using (var leftPath = new SKPath())
        {
            leftPath.MoveTo(0, 0);
            leftPath.LineTo(centerX + slashLean, 0);
            leftPath.LineTo(centerX - slashLean, height);
            leftPath.LineTo(0, height);
            leftPath.Close();

            canvas.Save();
            canvas.ClipPath(leftPath);
            using var img1 = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, 0, out _);
            if (img1 is not null)
            {
                DrawCenterCropped(canvas, img1, new SKRect(0, 0, width, height));
            }

            canvas.Restore();
        }

        // Right region: (centerX + lean, 0) → top-right → bottom-right → (centerX − lean, height)
        using (var rightPath = new SKPath())
        {
            rightPath.MoveTo(centerX + slashLean, 0);
            rightPath.LineTo(width, 0);
            rightPath.LineTo(width, height);
            rightPath.LineTo(centerX - slashLean, height);
            rightPath.Close();

            canvas.Save();
            canvas.ClipPath(rightPath);
            using var img2 = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, paths.Count > 1 ? 1 : 0, out _);
            if (img2 is not null)
            {
                DrawCenterCropped(canvas, img2, new SKRect(0, 0, width, height));
            }

            canvas.Restore();
        }

        // Draw a thin dark line along the cut for definition.
        using var linePaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(200),
            StrokeWidth = 3,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        canvas.DrawLine(centerX + slashLean, 0, centerX - slashLean, height, linePaint);

        return bitmap;
    }

    /// <summary>
    /// Create a hero-and-strip collage: a large poster on the left with a vertical strip of
    /// up to three thumbnails on the right.
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildHeroAndStripCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildHeroAndStripCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildHeroAndStripCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (paths.Count == 0)
        {
            return bitmap;
        }

        const int gap = 4;
        float heroWidth = width * 0.65f;
        float stripWidth = width - heroWidth - gap;
        float stripX = heroWidth + gap;

        // Hero: first image fills the left panel.
        using var hero = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, 0, out _);
        if (hero is not null)
        {
            DrawCenterCropped(canvas, hero, new SKRect(0, 0, heroWidth, height));
        }

        // Strip: up to 3 images stacked on the right. If there is only one source image, repeat
        // it so the strip is never empty.
        int stripCount = paths.Count > 1 ? Math.Min(paths.Count - 1, 3) : 1;
        float cellHeight = (height - (gap * (stripCount - 1))) / (float)stripCount;

        for (int i = 0; i < stripCount; i++)
        {
            int imgIdx = paths.Count > 1 ? i + 1 : 0;
            float y = i * (cellHeight + gap);
            var dstRect = new SKRect(stripX, y, stripX + stripWidth, y + cellHeight);

            using var img = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, imgIdx, out _);
            if (img is not null)
            {
                DrawCenterCropped(canvas, img, dstRect);
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Create a quad-grid collage: a 2×2 grid using the first four images.
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildQuadGridCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildQuadGridCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildQuadGridCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (paths.Count == 0)
        {
            return bitmap;
        }

        const int gap = 4;
        float cellWidth = (width - gap) / 2f;
        float cellHeight = (height - gap) / 2f;
        int imageIndex = 0;

        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 2; col++)
            {
                float x = col * (cellWidth + gap);
                float y = row * (cellHeight + gap);
                var dstRect = new SKRect(x, y, x + cellWidth, y + cellHeight);

                using var img = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, imageIndex, out int newIndex);
                imageIndex = newIndex;

                if (img is not null)
                {
                    DrawCenterCropped(canvas, img, dstRect);
                }
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Create a waterfall collage: four portrait columns with alternating vertical offset.
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildWaterfallCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildWaterfallCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildWaterfallCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        // Fewer unique images → fewer columns → each cover appears larger, less repetition.
        int numColumns = paths.Count switch
        {
            0 => 0,
            <= 2 => 1,
            <= 5 => 2,
            _ => 4
        };

        if (numColumns == 0)
        {
            return bitmap;
        }

        const int padding = 8;

        // Each cell is portrait (2:3 ratio).  colWidth includes half-padding on each side;
        // the actual drawn cell is (colWidth - padding) wide with padding on the right edge.
        int colWidth = width / numColumns;
        int cellWidth = colWidth - padding;
        int cellHeight = cellWidth * 3 / 2; // 2:3 portrait
        int rowStride = cellHeight + padding;

        // Odd columns are offset downward by half a row so they interleave with even columns.
        int verticalColOffset = rowStride / 2;

        // Each column starts cycling images at a different offset so that no two horizontally
        // adjacent cells (which overlap vertically due to the half-row stagger) show the same
        // image.  The minimum safe offset is 2 for 4-column layouts (the odd→even transition
        // creates an extra adjacency constraint); 1 suffices for 2-column layouts.
        int imageColOffset = Math.Max(numColumns >= 4 ? 2 : 1, paths.Count / numColumns);

        for (int col = 0; col < numColumns; col++)
        {
            float xStart = (col * colWidth) + (padding / 2f);
            float yStart = col % 2 == 1 ? -verticalColOffset : 0f;

            // Start each column at a different image so adjacent columns never repeat.
            int imageIndex = (col * imageColOffset) % paths.Count;

            // Fill the column with as many rows as needed to cover the canvas height.
            while (yStart < height)
            {
                using var currentBitmap = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, imageIndex, out int newIndex);
                imageIndex = newIndex;

                if (currentBitmap is not null)
                {
                    var dstRect = new SKRect(xStart, yStart, xStart + cellWidth, yStart + cellHeight);
                    DrawCenterCropped(canvas, currentBitmap, dstRect);
                }

                yStart += rowStride;
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Create an overlap-pile collage: two portrait posters rotated in opposite directions and
    /// overlapping in the center, like cards fanned on a table. The front card is drawn slightly
    /// larger to convey depth. Works best with 2 images; repeats the first when only 1 is available.
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildOverlapPileCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildOverlapPileCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildOverlapPileCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(18, 18, 18));

        if (paths.Count == 0)
        {
            return bitmap;
        }

        // Portrait card: 2:3 aspect, ~83% of canvas height.
        float cardH = height * 0.83f;
        float cardW = cardH * (2f / 3f);
        float halfW = cardW / 2f;
        float halfH = cardH / 2f;

        // Per-count layouts: (cxFrac, cyFrac, angleDeg, scale).
        // Draw order is back → front so the center/last card ends up on top.
        int numCards = Math.Min(paths.Count, 5);
        (float CxFrac, float CyFrac, float Deg, float Scale)[] layout = numCards switch
        {
            1 => [(0.50f, 0.50f, 0f, 1.00f)],
            2 => [(0.30f, 0.50f, -7f, 0.93f), (0.70f, 0.50f, 6f, 1.00f)],
            3 => [(0.20f, 0.50f, -14f, 0.88f), (0.80f, 0.50f, 13f, 0.90f), (0.50f, 0.48f, 3f, 0.96f)],
            4 => [(0.18f, 0.50f, -16f, 0.84f), (0.82f, 0.50f, 17f, 0.87f), (0.38f, 0.50f, -5f, 0.91f), (0.62f, 0.50f, 7f, 0.95f)],
            _ => [(0.14f, 0.50f, -18f, 0.80f), (0.86f, 0.50f, 19f, 0.83f), (0.30f, 0.50f, -8f, 0.87f), (0.70f, 0.50f, 11f, 0.91f), (0.50f, 0.48f, 2f, 0.96f)],
        };

        for (int i = 0; i < layout.Length; i++)
        {
            var (cxFrac, cyFrac, deg, scale) = layout[i];
            DrawRotatedCard(
                canvas,
                paths,
                pathIndex: i % paths.Count,
                cx: width * cxFrac,
                cy: height * cyFrac,
                halfW: halfW * scale,
                halfH: halfH * scale,
                angleDegrees: deg);
        }

        return bitmap;
    }

    /// <summary>
    /// Draws a single poster card centered at (<paramref name="cx"/>, <paramref name="cy"/>) and
    /// rotated by <paramref name="angleDegrees"/>. A soft drop-shadow is rendered beneath the card.
    /// </summary>
    private void DrawRotatedCard(
        SKCanvas canvas,
        IReadOnlyList<string> paths,
        int pathIndex,
        float cx,
        float cy,
        float halfW,
        float halfH,
        float angleDegrees)
    {
        using var img = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, pathIndex, out _);
        if (img is null)
        {
            return;
        }

        var cardRect = new SKRect(-halfW, -halfH, halfW, halfH);

        // Soft shadow: slightly larger, offset, and blurred.
        canvas.Save();
        canvas.Translate(cx + 7f, cy + 10f);
        canvas.RotateDegrees(angleDegrees);
        using var shadowPaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(100),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14f)
        };
        canvas.DrawRect(cardRect, shadowPaint);
        canvas.Restore();

        // Poster image.
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.RotateDegrees(angleDegrees);
        DrawCenterCropped(canvas, img, cardRect);
        canvas.Restore();
    }

    /// <summary>
    /// Create a hero-sliver collage: a large hero poster on the left (~70 % width) with 1–2
    /// portrait slivers on the right, vertically centered as a group and allowed to overflow
    /// the frame naturally, giving a partial-reveal effect.
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildHeroSliverCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildHeroSliverCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildHeroSliverCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (paths.Count == 0)
        {
            return bitmap;
        }

        const int gap = 8;
        float heroWidth = width * 0.70f;
        float sliverX = heroWidth + gap;
        float sliverWidth = width - sliverX;

        // Each sliver is 62 % of the canvas height — tall enough to feel substantial but short
        // enough that two slivers overflow the frame pleasantly (partial-reveal effect).
        float sliverHeight = height * 0.62f;

        // Hero: first image fills the left panel at full height.
        using var hero = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, 0, out _);
        if (hero is not null)
        {
            DrawCenterCropped(canvas, hero, new SKRect(0, 0, heroWidth, height));
        }

        // Slivers: up to 2 images on the right, centered as a group (overflow clips naturally).
        int sliverCount = Math.Min(paths.Count > 1 ? paths.Count - 1 : 1, 2);
        float totalGroupHeight = (sliverCount * sliverHeight) + ((sliverCount - 1) * gap);
        float startY = (height - totalGroupHeight) / 2f;

        for (int i = 0; i < sliverCount; i++)
        {
            int imgIdx = paths.Count > 1 ? i + 1 : 0;
            float y = startY + (i * (sliverHeight + gap));
            var dstRect = new SKRect(sliverX, y, sliverX + sliverWidth, y + sliverHeight);

            using var img = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, imgIdx, out _);
            if (img is not null)
            {
                DrawCenterCropped(canvas, img, dstRect);
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Create a hero-grid collage: a large hero poster on the left (~60% width) with a 2×2
    /// grid of up to four smaller posters on the right.
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildHeroGridCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildHeroGridCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildHeroGridCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (paths.Count == 0)
        {
            return bitmap;
        }

        const int gap = 4;
        const int gridCols = 2;
        const int gridRows = 2;
        float heroWidth = width * 0.60f;
        float gridX = heroWidth + gap;
        float gridPanelWidth = width - gridX;
        float cellWidth = (gridPanelWidth - ((gridCols - 1) * gap)) / gridCols;
        float cellHeight = (height - ((gridRows - 1) * gap)) / gridRows;

        // Hero: first image fills the left panel at full height.
        using var hero = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, 0, out _);
        if (hero is not null)
        {
            DrawCenterCropped(canvas, hero, new SKRect(0, 0, heroWidth, height));
        }

        // Grid: up to 4 images in a 2×2 arrangement on the right.
        for (int row = 0; row < gridRows; row++)
        {
            for (int col = 0; col < gridCols; col++)
            {
                int cellIdx = (row * gridCols) + col;
                int imgIdx = paths.Count > 1 ? 1 + (cellIdx % (paths.Count - 1)) : 0;
                float x = gridX + (col * (cellWidth + gap));
                float y = row * (cellHeight + gap);

                using var img = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, imgIdx, out _);
                if (img is not null)
                {
                    DrawCenterCropped(canvas, img, new SKRect(x, y, x + cellWidth, y + cellHeight));
                }
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Create an asymmetric-trio collage: one tall poster filling the left column, two
    /// half-height posters stacked on the right. Three films, editorial magazine aesthetic.
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildAsymmetricTrioCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildAsymmetricTrioCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildAsymmetricTrioCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (paths.Count == 0)
        {
            return bitmap;
        }

        const int gap = 4;
        float leftWidth = width * 0.45f;
        float rightX = leftWidth + gap;
        float rightWidth = width - rightX;
        float halfH = (height - gap) / 2f;

        // Left: first image at full height.
        using var left = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, 0, out _);
        if (left is not null)
        {
            DrawCenterCropped(canvas, left, new SKRect(0, 0, leftWidth, height));
        }

        // Top-right: second image (falls back to first if only one image).
        int topIdx = paths.Count > 1 ? 1 : 0;
        using var topRight = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, topIdx, out _);
        if (topRight is not null)
        {
            DrawCenterCropped(canvas, topRight, new SKRect(rightX, 0, rightX + rightWidth, halfH));
        }

        // Bottom-right: third image (falls back gracefully if fewer than 3 images).
        int botIdx = paths.Count > 2 ? 2 : (paths.Count > 1 ? 1 : 0);
        using var botRight = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, botIdx, out _);
        if (botRight is not null)
        {
            DrawCenterCropped(canvas, botRight, new SKRect(rightX, halfH + gap, rightX + rightWidth, halfH + gap + halfH));
        }

        return bitmap;
    }

    /// <summary>
    /// Create a mosaic-grid collage: a dense equal-cell portrait grid that fills the canvas
    /// with as many images as possible. Best suited for large collections (10+ movies).
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildMosaicGridCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildMosaicGridCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildMosaicGridCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (paths.Count == 0)
        {
            return bitmap;
        }

        const int gap = 3;
        // Larger collections get more columns for a denser feel.
        int cols = paths.Count >= 12 ? 6 : 5;
        float cellWidth = (width - ((cols - 1) * gap)) / (float)cols;
        float cellHeight = cellWidth * 1.5f; // 2:3 portrait aspect
        int rows = (int)Math.Ceiling((height + gap) / (cellHeight + gap));

        int imageIndex = 0;
        for (int row = 0; row < rows; row++)
        {
            float y = row * (cellHeight + gap);
            for (int col = 0; col < cols; col++)
            {
                float x = col * (cellWidth + gap);

                using var img = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, imageIndex, out int newIndex);
                imageIndex = newIndex;

                if (img is not null)
                {
                    DrawCenterCropped(canvas, img, new SKRect(x, y, x + cellWidth, y + cellHeight));
                }
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Create a waterfall-fade collage: the standard waterfall layout with horizontal
    /// gradient fades on both edges that tame visual noise for large collections.
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildWaterfallFadeCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildWaterfallFadeCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildWaterfallFadeCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        // Render the waterfall onto a base bitmap, then composite the gradient overlays.
        using var waterfallBitmap = BuildWaterfallCollageBitmap(paths, width, height);

        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        using var basePaint = new SKPaint();
        canvas.DrawBitmap(waterfallBitmap, 0, 0, SkiaEncoder.DefaultSamplingOptions, basePaint);

        // Gradient fades: opaque black at each edge fading to transparent toward center.
        float fadeWidth = width * 0.22f;

        using var leftShader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(fadeWidth, 0),
            new[] { SKColors.Black, SKColors.Transparent },
            null,
            SKShaderTileMode.Clamp);
        using var leftPaint = new SKPaint { Shader = leftShader };
        canvas.DrawRect(new SKRect(0, 0, fadeWidth, height), leftPaint);

        using var rightShader = SKShader.CreateLinearGradient(
            new SKPoint(width - fadeWidth, 0),
            new SKPoint(width, 0),
            new[] { SKColors.Transparent, SKColors.Black },
            null,
            SKShaderTileMode.Clamp);
        using var rightPaint = new SKPaint { Shader = rightShader };
        canvas.DrawRect(new SKRect(width - fadeWidth, 0, width, height), rightPaint);

        return bitmap;
    }

    /// <summary>
    /// Create a condensed-strip collage: a single horizontal band of portrait poster tops
    /// across the vertical center of the canvas, evoking a film-strip aesthetic.
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildCondensedStripCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildCondensedStripCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildCondensedStripCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (paths.Count == 0)
        {
            return bitmap;
        }

        // Strip: 2:3 portrait cells at 35% canvas height, tightly packed across full width.
        // One extra cell is rendered beyond the right edge to give a partial-reveal at the end.
        float stripH = height * 0.35f;
        float cellW = stripH * (2f / 3f); // maintain 2:3 aspect
        float stripY = (height - stripH) / 2f;
        int cellCount = (int)Math.Ceiling(width / cellW) + 1;

        for (int i = 0; i < cellCount; i++)
        {
            float x = i * cellW;
            int imgIdx = i % paths.Count;

            using var img = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, imgIdx, out _);
            if (img is not null)
            {
                DrawTopCropped(canvas, img, new SKRect(x, stripY, x + cellW, stripY + stripH));
            }
        }

        // Soft vertical fades above and below the strip to blend into the black background.
        float fadeH = stripY * 0.75f;

        using var topShader = SKShader.CreateLinearGradient(
            new SKPoint(0, stripY - fadeH),
            new SKPoint(0, stripY),
            new[] { SKColors.Black, SKColors.Transparent },
            null,
            SKShaderTileMode.Clamp);
        using var topPaint = new SKPaint { Shader = topShader };
        canvas.DrawRect(new SKRect(0, stripY - fadeH, width, stripY), topPaint);

        using var botShader = SKShader.CreateLinearGradient(
            new SKPoint(0, stripY + stripH),
            new SKPoint(0, stripY + stripH + fadeH),
            new[] { SKColors.Transparent, SKColors.Black },
            null,
            SKShaderTileMode.Clamp);
        using var botPaint = new SKPaint { Shader = botShader };
        canvas.DrawRect(new SKRect(0, stripY + stripH, width, stripY + stripH + fadeH), botPaint);

        return bitmap;
    }

    /// <summary>
    /// Create a backdrop-panorama collage: the first image is center-cropped to fill the
    /// landscape canvas with a gradient overlay and the collection title rendered on top.
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    /// <param name="title">The collection title to render over the image.</param>
    public void BuildBackdropPanoramaCollage(
        IReadOnlyList<string> paths,
        string outputPath,
        int width,
        int height,
        string? title)
    {
        using var bitmap = BuildBackdropPanoramaCollageBitmap(paths, width, height, title);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildBackdropPanoramaCollageBitmap(
        IReadOnlyList<string> paths,
        int width,
        int height,
        string? title)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (paths.Count == 0)
        {
            return bitmap;
        }

        // Fill canvas with the first image, center-cropped to the landscape frame.
        using var bg = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, 0, out _);
        if (bg is not null)
        {
            DrawCenterCropped(canvas, bg, new SKRect(0, 0, width, height));
        }

        // Gradient overlay: transparent at the top, darkening toward the bottom so the
        // title text is legible regardless of image content.
        using var gradientShader = SKShader.CreateLinearGradient(
            new SKPoint(0, height * 0.35f),
            new SKPoint(0, height),
            new[] { SKColors.Transparent, SKColors.Black.WithAlpha(210) },
            null,
            SKShaderTileMode.Clamp);
        using var gradientPaint = new SKPaint { Shader = gradientShader };
        canvas.DrawRect(new SKRect(0, 0, width, height), gradientPaint);

        // Collection title near the bottom.
        if (!string.IsNullOrWhiteSpace(title))
        {
            using var textFont = new SKFont
            {
                Size = 62,
                Typeface = SkiaEncoder.DefaultTypeFace
            };
            using var textPaint = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            float textY = height * 0.88f;

            // Measure and scale down if the title is wider than 90% of the canvas.
            var measuredWidth = DrawText(null, 0, textY, title, textPaint, textFont);
            if (measuredWidth > width * 0.90f)
            {
                textFont.Size = textFont.Size * (width * 0.90f) / measuredWidth;
                measuredWidth = DrawText(null, 0, textY, title, textPaint, textFont);
            }

            float textX = (width - measuredWidth) / 2f;

            if (IsRtlTextRegex().IsMatch(title))
            {
                DrawText(canvas, width - textX, textY, title, textPaint, textFont, isRtl: true);
            }
            else
            {
                DrawText(canvas, textX, textY, title, textPaint, textFont);
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Create a spotlight collage: one poster displayed center-stage at full height, with
    /// blurred and darkened copies on each side creating a stage-light depth effect.
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildSpotlightCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildSpotlightCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildSpotlightCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (paths.Count == 0)
        {
            return bitmap;
        }

        // Center poster: 2:3 portrait at 88% of canvas height.
        float posterH = height * 0.88f;
        float posterW = posterH * (2f / 3f);
        float halfPosterW = posterW / 2f;
        float posterY = (height - posterH) / 2f;
        float centerX = width / 2f;

        // Side copies: each offset so roughly 30% of the poster peeks in from each edge.
        float sideOffset = posterW * 0.72f;

        // Left blurred/darkened copy (uses second image if available).
        int leftIdx = paths.Count > 1 ? 1 : 0;
        using var leftImg = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, leftIdx, out _);
        if (leftImg is not null)
        {
            var leftRect = new SKRect(
                centerX - sideOffset - halfPosterW,
                posterY,
                centerX - sideOffset + halfPosterW,
                posterY + posterH);
            DrawBlurredAndDarkened(canvas, leftImg, leftRect, blurSigma: 18f);
        }

        // Right blurred/darkened copy.
        int rightIdx = paths.Count > 2 ? 2 : (paths.Count > 1 ? 1 : 0);
        using var rightImg = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, rightIdx, out _);
        if (rightImg is not null)
        {
            var rightRect = new SKRect(
                centerX + sideOffset - halfPosterW,
                posterY,
                centerX + sideOffset + halfPosterW,
                posterY + posterH);
            DrawBlurredAndDarkened(canvas, rightImg, rightRect, blurSigma: 18f);
        }

        // Center poster: sharp and full brightness, drawn on top.
        using var centerImg = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, 0, out _);
        if (centerImg is not null)
        {
            DrawCenterCropped(
                canvas,
                centerImg,
                new SKRect(centerX - halfPosterW, posterY, centerX + halfPosterW, posterY + posterH));
        }

        // Stage-light vignette: opaque black at both edges fading to transparent near the center.
        float vignetteW = width * 0.32f;

        using var leftVignetteShader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(vignetteW, 0),
            new[] { SKColors.Black, SKColors.Transparent },
            null,
            SKShaderTileMode.Clamp);
        using var leftVignettePaint = new SKPaint { Shader = leftVignetteShader };
        canvas.DrawRect(new SKRect(0, 0, vignetteW, height), leftVignettePaint);

        using var rightVignetteShader = SKShader.CreateLinearGradient(
            new SKPoint(width - vignetteW, 0),
            new SKPoint(width, 0),
            new[] { SKColors.Transparent, SKColors.Black },
            null,
            SKShaderTileMode.Clamp);
        using var rightVignettePaint = new SKPaint { Shader = rightVignetteShader };
        canvas.DrawRect(new SKRect(width - vignetteW, 0, width, height), rightVignettePaint);

        return bitmap;
    }

    /// <summary>
    /// Create a bookshelf-spine collage: the narrow vertical center slice of each poster is
    /// laid side-by-side across the full canvas height, evoking a DVD shelf.
    /// </summary>
    /// <param name="paths">The paths of the images to use.</param>
    /// <param name="outputPath">The path at which to place the resulting image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildBookshelfSpineCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildBookshelfSpineCollageBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildBookshelfSpineCollageBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (paths.Count == 0)
        {
            return bitmap;
        }

        // Cap at 20 spines; fewer images → wider spines → more visible detail.
        int spineCount = Math.Min(paths.Count, 20);
        float spineWidth = width / (float)spineCount;

        for (int i = 0; i < spineCount; i++)
        {
            float x = i * spineWidth;

            using var img = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, i % paths.Count, out _);
            if (img is null)
            {
                continue;
            }

            // Crop the vertical center slice from each poster.
            // Scale the image to fill canvas height, then take the center spineWidth strip.
            float scaledWidth = (float)img.Width * height / img.Height;
            float cropFraction = spineWidth / scaledWidth;
            float srcCropW = img.Width * cropFraction;
            float srcCropX = (img.Width - srcCropW) / 2f;
            var srcRect = new SKRect(srcCropX, 0, srcCropX + srcCropW, img.Height);

            using var paint = new SKPaint();
            canvas.DrawBitmap(img, srcRect, new SKRect(x, 0, x + spineWidth, height), SkiaEncoder.DefaultSamplingOptions, paint);
        }

        return bitmap;
    }

    /// <summary>
    /// Crops <paramref name="src"/> to fill <paramref name="dstRect"/>, showing the top
    /// portion of the source image rather than the center. Used for the condensed-strip
    /// layout to reveal character faces and title text from each poster.
    /// </summary>
    private static void DrawTopCropped(SKCanvas canvas, SKBitmap src, SKRect dstRect)
    {
        float srcAspect = (float)src.Width / src.Height;
        float dstAspect = dstRect.Width / dstRect.Height;

        SKRect srcRect;
        if (srcAspect > dstAspect)
        {
            // Source is wider than destination: center-crop horizontally.
            float cropWidth = src.Height * dstAspect;
            float cropX = (src.Width - cropWidth) / 2f;
            srcRect = new SKRect(cropX, 0, cropX + cropWidth, src.Height);
        }
        else
        {
            // Source is taller than destination: show the top portion, not the center.
            float cropHeight = src.Width / dstAspect;
            srcRect = new SKRect(0, 0, src.Width, cropHeight);
        }

        using var paint = new SKPaint();
        canvas.DrawBitmap(src, srcRect, dstRect, SkiaEncoder.DefaultSamplingOptions, paint);
    }

    /// <summary>
    /// Draws <paramref name="src"/> into <paramref name="dstRect"/> with a Gaussian blur
    /// and a semi-transparent black overlay to produce a blurred, darkened result.
    /// </summary>
    private static void DrawBlurredAndDarkened(SKCanvas canvas, SKBitmap src, SKRect dstRect, float blurSigma)
    {
        float srcAspect = (float)src.Width / src.Height;
        float dstAspect = dstRect.Width / dstRect.Height;

        SKRect srcRect;
        if (srcAspect > dstAspect)
        {
            float cropWidth = src.Height * dstAspect;
            float cropX = (src.Width - cropWidth) / 2f;
            srcRect = new SKRect(cropX, 0, cropX + cropWidth, src.Height);
        }
        else
        {
            float cropHeight = src.Width / dstAspect;
            float cropY = (src.Height - cropHeight) / 2f;
            srcRect = new SKRect(0, cropY, src.Width, cropY + cropHeight);
        }

        using var blurFilter = SKImageFilter.CreateBlur(blurSigma, blurSigma);
        using var blurPaint = new SKPaint { ImageFilter = blurFilter };
        canvas.DrawBitmap(src, srcRect, dstRect, SkiaEncoder.DefaultSamplingOptions, blurPaint);

        using var darkPaint = new SKPaint { Color = SKColors.Black.WithAlpha(150) };
        canvas.DrawRect(dstRect, darkPaint);
    }

    /// <summary>
    /// Card drop: up to six posters scattered at varied rotations across a dark canvas, like
    /// photos dropped on a table. Each card has a drop shadow; the last card is drawn on top.
    /// </summary>
    /// <param name="paths">The paths of the images to use in the collage.</param>
    /// <param name="outputPath">The path at which to place the resulting collage image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildCardDropCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildCardDropBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildCardDropBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(18, 18, 18));

        if (paths.Count == 0)
        {
            return bitmap;
        }

        int numCards = Math.Min(paths.Count, 6);

        // Cards are slightly smaller for larger piles so more of each card shows.
        float cardH = height * (numCards <= 3 ? 0.70f : 0.60f);
        float cardW = cardH * (2f / 3f);
        float halfH = cardH / 2f;
        float halfW = cardW / 2f;

        // Fixed scatter positions (cxFrac, cyFrac, angleDeg), back → front draw order.
        // Positions chosen so all cards are substantially visible within the canvas.
        (float CxFrac, float CyFrac, float Deg)[] positions = numCards switch
        {
            1 => [(0.50f, 0.50f, 0f)],
            2 => [(0.28f, 0.54f, -18f), (0.72f, 0.48f, 14f)],
            3 => [(0.20f, 0.56f, -20f), (0.72f, 0.44f, 17f), (0.46f, 0.52f, -4f)],
            4 => [(0.16f, 0.57f, -22f), (0.80f, 0.40f, 21f), (0.36f, 0.45f, 13f), (0.60f, 0.56f, -9f)],
            5 => [(0.12f, 0.60f, -24f), (0.84f, 0.36f, 23f), (0.30f, 0.43f, 15f), (0.70f, 0.60f, -12f), (0.48f, 0.50f, -2f)],
            _ => [(0.10f, 0.62f, -24f), (0.85f, 0.34f, 23f), (0.26f, 0.42f, 17f), (0.74f, 0.62f, -14f), (0.44f, 0.36f, 9f), (0.56f, 0.60f, -5f)],
        };

        for (int i = 0; i < positions.Length; i++)
        {
            var (cxFrac, cyFrac, deg) = positions[i];
            DrawRotatedCard(
                canvas,
                paths,
                pathIndex: i % paths.Count,
                cx: width * cxFrac,
                cy: height * cyFrac,
                halfW: halfW,
                halfH: halfH,
                angleDegrees: deg);
        }

        return bitmap;
    }

    /// <summary>
    /// Fan spread: up to six posters fanned from a common pivot below the canvas, like a hand
    /// of cards held vertically. The center card is drawn last (on top).
    /// </summary>
    /// <param name="paths">The paths of the images to use in the collage.</param>
    /// <param name="outputPath">The path at which to place the resulting collage image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildFanSpreadCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        using var bitmap = BuildFanSpreadBitmap(paths, width, height);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    private SKBitmap BuildFanSpreadBitmap(IReadOnlyList<string> paths, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(18, 18, 18));

        if (paths.Count == 0)
        {
            return bitmap;
        }

        int numCards = Math.Min(paths.Count, 6);

        float cardH = height * 0.88f;
        float cardW = cardH * (2f / 3f);
        float halfH = cardH / 2f;
        float halfW = cardW / 2f;

        // Pivot well below the canvas; radius positions card centers above the bottom edge.
        float pivotX = width * 0.50f;
        float pivotY = height * 1.65f;
        float radius = height * 1.28f;

        // Total angular spread grows with card count, capped to keep edge cards in frame.
        float totalSpreadDeg = Math.Min(50f, (numCards - 1) * 14f);
        float angleStepDeg = numCards > 1 ? totalSpreadDeg / (numCards - 1) : 0f;

        // Draw order: edges first, center card last (drawn on top).
        int[] drawOrder = new int[numCards];
        int lo = 0, hi = numCards - 1, pos = 0;
        while (lo <= hi)
        {
            if (lo == hi)
            {
                drawOrder[pos++] = lo++;
            }
            else
            {
                drawOrder[pos++] = lo++;
                drawOrder[pos++] = hi--;
            }
        }

        foreach (int i in drawOrder)
        {
            float angleDeg = -(totalSpreadDeg / 2f) + (i * angleStepDeg);
            float angleRad = (angleDeg * (float)Math.PI) / 180f;
            float cx = pivotX + ((float)Math.Sin(angleRad) * radius);
            float cy = pivotY - ((float)Math.Cos(angleRad) * radius);
            DrawRotatedCard(
                canvas,
                paths,
                pathIndex: i % paths.Count,
                cx: cx,
                cy: cy,
                halfW: halfW,
                halfH: halfH,
                angleDegrees: angleDeg);
        }

        return bitmap;
    }

    /// <summary>
    /// Diagonal forward: six portrait strips each leaning right, like parallel forward slashes
    /// ( / / / / / / ).
    /// </summary>
    /// <param name="paths">The paths of the images to use in the collage.</param>
    /// <param name="outputPath">The path at which to place the resulting collage image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildDiagonalForwardCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        bool[] dirs = [true, true, true, true, true, true];
        using var bitmap = BuildDiagonalSlashBitmap(paths, width, height, dirs, width * 0.18f);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    /// <summary>
    /// Diagonal backward: six portrait strips each leaning left, like parallel backslashes
    /// ( \ \ \ \ \ \ ).
    /// </summary>
    /// <param name="paths">The paths of the images to use in the collage.</param>
    /// <param name="outputPath">The path at which to place the resulting collage image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildDiagonalBackwardCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        bool[] dirs = [false, false, false, false, false, false];
        using var bitmap = BuildDiagonalSlashBitmap(paths, width, height, dirs, width * 0.18f);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    /// <summary>
    /// Diagonal alternating: strips alternate between forward and backward lean
    /// ( / \ / \ / \ ), creating a chevron rhythm.
    /// </summary>
    /// <param name="paths">The paths of the images to use in the collage.</param>
    /// <param name="outputPath">The path at which to place the resulting collage image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildDiagonalAlternatingCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        bool[] dirs = [true, false, true, false, true, false];
        using var bitmap = BuildDiagonalSlashBitmap(paths, width, height, dirs, (float)width * 0.05f);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    /// <summary>
    /// Diagonal mixed: strips use an asymmetric forward/backward pattern
    /// ( / / \ \ / \ ), producing a more complex rhythmic split.
    /// </summary>
    /// <param name="paths">The paths of the images to use in the collage.</param>
    /// <param name="outputPath">The path at which to place the resulting collage image.</param>
    /// <param name="width">The desired width of the collage.</param>
    /// <param name="height">The desired height of the collage.</param>
    public void BuildDiagonalMixedCollage(IReadOnlyList<string> paths, string outputPath, int width, int height)
    {
        bool[] dirs = [true, true, false, false, true, false];
        using var bitmap = BuildDiagonalSlashBitmap(paths, width, height, dirs, width * 0.12f);
        using var outputStream = new SKFileWStream(outputPath);
        using var pixmap = new SKPixmap(new SKImageInfo(width, height), bitmap.GetPixels());
        pixmap.Encode(outputStream, GetEncodedFormat(outputPath), 90);
    }

    /// <summary>
    /// Core diagonal-slash renderer. Each entry in <paramref name="directions"/> defines one strip:
    /// <c>true</c> = forward slash (/), <c>false</c> = backward slash (\).
    /// Strip boundaries are computed so that at /\ diverging transitions the later strip
    /// extends to overlap the earlier one, eliminating black diamond gaps. At most four
    /// unique images are cycled through the strips to avoid visual clutter on large collections.
    /// </summary>
    private SKBitmap BuildDiagonalSlashBitmap(
        IReadOnlyList<string> paths,
        int width,
        int height,
        bool[] directions,
        float lean)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (paths.Count == 0)
        {
            return bitmap;
        }

        int numStrips = directions.Length;
        float stripW = (float)width / numStrips;

        // Cap unique images to 4 so large collections don't produce visual clutter.
        int maxUnique = Math.Min(paths.Count, 4);

        // Pre-compute the x position at y=0 for each strip boundary (indices 0..numStrips).
        // At any boundary where at least one adjacent strip is a / strip, the boundary top
        // shifts right by lean — this ensures /\ transitions overlap rather than gap.
        // At boundaries where both adjacent strips are \ strips, the boundary top shifts left.
        var boundaryTopX = new float[numStrips + 1];
        boundaryTopX[0] = 0;
        boundaryTopX[numStrips] = width;
        for (int b = 1; b < numStrips; b++)
        {
            float xBound = b * stripW;
            bool anyForward = directions[b - 1] || directions[b];
            boundaryTopX[b] = xBound + (anyForward ? lean : -lean);
        }

        for (int i = 0; i < numStrips; i++)
        {
            float xLeft = i * stripW;
            float xRight = (i + 1) * stripW;

            using var path = new SKPath();
            path.MoveTo(xLeft, height);
            path.LineTo(xRight, height);
            path.LineTo(boundaryTopX[i + 1], 0);
            path.LineTo(boundaryTopX[i], 0);
            path.Close();

            using var img = SkiaHelper.GetNextValidImage(_skiaEncoder, paths, i % maxUnique, out _);
            if (img is null)
            {
                continue;
            }

            canvas.Save();
            canvas.ClipPath(path);
            DrawCenterCropped(canvas, img, new SKRect(0, 0, width, height));
            canvas.Restore();
        }

        // Draw subtle boundary lines using the same pre-computed top positions.
        using var linePaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(160),
            StrokeWidth = 2f,
            IsStroke = true,
            IsAntialias = true
        };

        for (int b = 1; b < numStrips; b++)
        {
            canvas.DrawLine(b * stripW, height, boundaryTopX[b], 0, linePaint);
        }

        return bitmap;
    }

    /// <summary>
    /// Draw shaped text with given SKPaint.
    /// </summary>
    /// <param name="canvas">If not null, draw text to this canvas, otherwise only measure the text width.</param>
    /// <param name="x">x position of the canvas to draw text.</param>
    /// <param name="y">y position of the canvas to draw text.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="textPaint">The SKPaint to style the text.</param>
    /// <param name="textFont">The SKFont to style the text.</param>
    /// <param name="alignment">The alignment of the text. Default aligns to left.</param>
    /// <returns>The width of the text.</returns>
    private static float MeasureAndDrawText(SKCanvas? canvas, float x, float y, string text, SKPaint textPaint, SKFont textFont, SKTextAlign alignment = SKTextAlign.Left)
    {
        var width = textFont.MeasureText(text);
        canvas?.DrawShapedText(text, x, y, alignment, textFont, textPaint);
        return width;
    }

    /// <summary>
    /// Draw shaped text with given SKPaint, search defined type faces to render as many texts as possible.
    /// </summary>
    /// <param name="canvas">If not null, draw text to this canvas, otherwise only measure the text width.</param>
    /// <param name="x">x position of the canvas to draw text.</param>
    /// <param name="y">y position of the canvas to draw text.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="textPaint">The SKPaint to style the text.</param>
    /// <param name="textFont">The SKFont to style the text.</param>
    /// <param name="isRtl">If true, render from right to left.</param>
    /// <returns>The width of the text.</returns>
    private static float DrawText(SKCanvas? canvas, float x, float y, string text, SKPaint textPaint, SKFont textFont, bool isRtl = false)
    {
        float width = 0;
        var alignment = isRtl ? SKTextAlign.Right : SKTextAlign.Left;

        if (textFont.ContainsGlyphs(text))
        {
            // Current font can render all characters in text
            return MeasureAndDrawText(canvas, x, y, text, textPaint, textFont, alignment);
        }

        // Iterate over all text elements using TextElementEnumerator
        // We cannot use foreach here because a human-readable character (grapheme cluster) can be multiple code points
        // We cannot render character by character because glyphs do not always have same width
        // And the result will look very unnatural due to the width difference and missing natural spacing
        var start = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            bool notAtEnd;
            var textElement = enumerator.GetTextElement();
            if (textFont.ContainsGlyphs(textElement))
            {
                continue;
            }

            // If we get here, we have a text element which cannot be rendered with current font
            // Draw previous characters which can be rendered with current font
            if (start != enumerator.ElementIndex)
            {
                var regularText = text.Substring(start, enumerator.ElementIndex - start);
                width += MeasureAndDrawText(canvas, MoveX(x, width), y, regularText, textPaint, textFont, alignment);
                start = enumerator.ElementIndex;
            }

            // Search for next point where current font can render the character there
            while ((notAtEnd = enumerator.MoveNext()) && !textFont.ContainsGlyphs(enumerator.GetTextElement()))
            {
                // Do nothing, just move enumerator to the point where current font can render the character
            }

            // Now we have a substring that should pick another font
            // The enumerator may or may not be already at the end of the string
            var subtext = notAtEnd
                ? text.Substring(start, enumerator.ElementIndex - start)
                : text[start..];

            var fallback = SkiaEncoder.GetFontForCharacter(textElement);

            if (fallback is not null)
            {
                using var fallbackTextFont = new SKFont();
                fallbackTextFont.Size = textFont.Size;
                fallbackTextFont.Typeface = fallback;
                using var fallbackTextPaint = new SKPaint();
                fallbackTextPaint.Color = textPaint.Color;
                fallbackTextPaint.Style = textPaint.Style;
                fallbackTextPaint.IsAntialias = textPaint.IsAntialias;

                // Do the search recursively to select all possible fonts
                width += DrawText(canvas, MoveX(x, width), y, subtext, fallbackTextPaint, fallbackTextFont, isRtl);
            }
            else
            {
                // Used up all fonts and no fonts can be found, just use current font
                width += MeasureAndDrawText(canvas, MoveX(x, width), y, text[start..], textPaint, textFont, alignment);
            }

            start = notAtEnd ? enumerator.ElementIndex : text.Length;
        }

        // Render the remaining text that current fonts can render
        if (start < text.Length)
        {
            width += MeasureAndDrawText(canvas, MoveX(x, width), y, text[start..], textPaint, textFont, alignment);
        }

        return width;
        float MoveX(float currentX, float dWidth) => isRtl ? currentX - dWidth : currentX + dWidth;
    }
}
