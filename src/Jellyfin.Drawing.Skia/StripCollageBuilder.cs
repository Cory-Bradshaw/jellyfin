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
