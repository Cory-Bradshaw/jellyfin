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
        QuadGrid = 5,

        /// <summary>Two posters rotated ±6–7° and overlapping in the center, like cards fanned on a table. Best for 2-image collections.</summary>
        OverlapPile = 6,

        /// <summary>Hero poster fills ~70% of the width; 1–2 portrait slivers on the right at ~62% card height, vertically centered with overflow clipping. Best for 2–3 movie collections.</summary>
        HeroSliver = 7,

        /// <summary>Large hero poster on the left (~60% width) with a 2×2 grid of up to four smaller posters on the right. Best for 5–9 movie collections.</summary>
        HeroGrid = 8,

        /// <summary>One tall poster filling the left column (~45% width); two stacked half-height posters on the right. Editorial magazine aesthetic. Best for 3+ movie collections.</summary>
        AsymmetricTrio = 9,

        /// <summary>Dense equal-cell portrait grid filling the canvas with as many images as possible. Best for large collections (10+ movies).</summary>
        MosaicGrid = 10,

        /// <summary>Standard waterfall layout with horizontal gradient fades to black on both edges. Best for large collections.</summary>
        WaterfallFade = 11,

        /// <summary>A single horizontal band of poster tops across the vertical center. Tightly packed film-strip aesthetic. Best for large collections.</summary>
        CondensedStrip = 12,

        /// <summary>First image center-cropped to fill the canvas with a gradient overlay and the collection title rendered on top. Works at any collection size.</summary>
        BackdropPanorama = 13,

        /// <summary>One poster center-stage at full height; blurred and darkened copies on each side create a stage-light depth effect. Works at any collection size.</summary>
        Spotlight = 14,

        /// <summary>Narrow vertical center slices of each poster laid side-by-side, evoking a DVD shelf. Works at any collection size.</summary>
        BookshelfSpine = 15,

        /// <summary>Up to six posters scattered at varied rotations across a dark canvas, like photos dropped on a table. Works at any collection size.</summary>
        CardDrop = 22,

        /// <summary>Up to six posters fanned from a common pivot below the canvas, like a hand of cards. Works at any collection size.</summary>
        FanSpread = 23,

        /// <summary>Six equal-width portrait strips leaning uniformly to the right ( / / / / / / ). Works at any collection size.</summary>
        DiagonalForward = 24,

        /// <summary>Six equal-width portrait strips leaning uniformly to the left ( \ \ \ \ \ \ ). Works at any collection size.</summary>
        DiagonalBackward = 25,

        /// <summary>Six strips with alternating lean directions ( / \ / \ / \ ), creating a chevron rhythm. Works at any collection size.</summary>
        DiagonalAlternating = 26,

        /// <summary>Six strips with an asymmetric mix of forward and backward lean ( / / \ \ / \ ). Works at any collection size.</summary>
        DiagonalMixed = 27
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
