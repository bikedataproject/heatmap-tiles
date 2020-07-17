using NetTopologySuite.Geometries;

namespace HeatMap.Tiles
{
    /// <summary>
    /// A transformation utility from WGS84 coordinates to a local tile coordinate system in pixel
    /// </summary>
    internal readonly struct TileGeometryTransform
    {
        public TileGeometryTransform(int zoom, uint tileId, uint extent) : this()
        {            
            var box = TileStatic.Box(zoom, tileId);
            this.Left = box.topLeft.longitude;
            var right = box.bottomRight.longitude;
            var bottom = box.bottomRight.latitude;
            this.Top = box.topLeft.latitude;
            
            LatitudeStep = (this.Top - bottom) / extent;
            LongitudeStep = (right - this.Left) / extent;
        }

        /// <summary>
        /// Gets a value indicating the latitude of the top-left corner of the tile
        /// </summary>
        public double Top { get; }

        /// <summary>
        /// Gets a value indicating the longitude of the top-left corner of the tile
        /// </summary>
        public double Left { get; }

        /// <summary>
        /// Gets a value indicating the height of tile's pixel 
        /// </summary>
        public double LatitudeStep { get; }

        /// <summary>
        /// Gets a value indicating the width of tile's pixel 
        /// </summary>
        public double LongitudeStep { get; }

        /// <summary>
        /// Transforms the coordinate at <paramref name="index"/> of <paramref name="sequence"/> to the tile coordinate system.
        /// The return value is the position relative to the local point at (<paramref name="currentX"/>, <paramref name="currentY"/>).
        /// </summary>
        /// <param name="sequence">The input sequence</param>
        /// <param name="index">The index of the coordinate to transform</param>
        /// <param name="currentX">The current horizontal component of the cursor location. This value is updated.</param>
        /// <param name="currentY">The current vertical component of the cursor location. This value is updated.</param>
        /// <returns>The position relative to the local point at (<paramref name="currentX"/>, <paramref name="currentY"/>).</returns>
        public (int x, int y) Transform(CoordinateSequence sequence, int index, ref int currentX, ref int currentY)
        {
            var localX = (int) ((sequence.GetOrdinate(index, Ordinate.X) - Left) / LongitudeStep);
            var localY = (int) ((Top - sequence.GetOrdinate(index, Ordinate.Y)) / LatitudeStep);
            var dx = localX - currentX;
            var dy = localY - currentY;
            currentX = localX;
            currentY = localY;

            return (dx, dy);
        }

        /// <summary>
        /// Transforms to a WGS84 coordinate.
        /// </summary>
        /// <param name="x">The x coordinate.</param>
        /// <param name="y">The y coordinate.</param>
        /// <returns>The </returns>
        public (double longitude, double latitude) TransformTo(int x, int y)
        {
            var lonOffset = LongitudeStep * x;
            var latOffset = LatitudeStep * y;

            return (this.Left + lonOffset, this.Top - latOffset); 
        }
    }
}