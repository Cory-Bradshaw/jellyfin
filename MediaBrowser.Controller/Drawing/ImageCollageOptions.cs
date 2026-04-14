#nullable disable

using System.Collections.Generic;

#pragma warning disable CS1591

namespace MediaBrowser.Controller.Drawing
{
    /// <summary>
    /// The type of collage layout to build.
    /// </summary>
    public enum CollageType
    {
        /// <summary>Default ratio-based dispatch (thumb or square).</summary>
        Default = 0,

        /// <summary>Portrait columns with alternating vertical offset. Scales from 1–4 columns based on image count. Best for medium/large collections.</summary>
        Waterfall = 1,

        /// <summary>Equal vertical split: 2 columns for 2-movie collections, 3 columns for 3-movie collections. Best for small collections.</summary>
        EvenSplit = 2,

        /// <summary>Diagonal slash dividing two posters. Always uses the first two images. Best for 2–3 movie collections.</summary>
        DiagonalCut = 3,

        /// <summary>Large hero poster on the left (~65% width) with up to three stacked thumbnails on the right. Works at any collection size.</summary>
        HeroAndStrip = 4,

        /// <summary>2×2 equal grid using the first four images. Best for 4–8 movie collections.</summary>
        QuadGrid = 5
    }

    public class ImageCollageOptions
    {
        /// <summary>
        /// Gets or sets the input paths.
        /// </summary>
        /// <value>The input paths.</value>
        public IReadOnlyList<string> InputPaths { get; set; }

        /// <summary>
        /// Gets or sets the output path.
        /// </summary>
        /// <value>The output path.</value>
        public string OutputPath { get; set; }

        /// <summary>
        /// Gets or sets the width.
        /// </summary>
        /// <value>The width.</value>
        public int Width { get; set; }

        /// <summary>
        /// Gets or sets the height.
        /// </summary>
        /// <value>The height.</value>
        public int Height { get; set; }

        /// <summary>
        /// Gets or sets the collage layout type.
        /// </summary>
        public CollageType CollageType { get; set; } = CollageType.Default;
    }
}
