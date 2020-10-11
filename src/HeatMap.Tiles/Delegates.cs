namespace HeatMap.Tiles
{
    /// <summary>
    /// Contains reusable delegates.
    /// </summary>
    public static class Delegates
    {
        /// <summary>
        /// A function to map values in a tile.
        /// </summary>
        /// <param name="location">The location in the tile.</param>
        /// <param name="value">The original value in the tile.</param>
        public delegate TOut ValueFunc<in TIn, out TOut>((int x, int y) location, TIn value);

        /// <summary>
        /// A function to map values in a tile.
        /// </summary>
        /// <param name="tile">The tile coordinates.</param>
        /// <param name="location">The location in the tile.</param>
        /// <param name="value">The original value in the tile.</param>
        public delegate TOut ValuePerTileFunc<in TIn, out TOut>((uint x, uint y, int z) tile, (int x, int y) location, TIn value);

        /// <summary>
        /// A function to map values in a tile.
        /// </summary>
        /// <param name="tile">The tile coordinates.</param>
        /// <param name="location">The location in the tile.</param>
        /// <param name="originalValue">The original value in the tile.</param>
        /// <param name="sourceValue">The source value in the tile.</param>
        public delegate TTarget AddPerTileFunc<in TSource, TTarget>((uint x, uint y, int z) tile, (int x, int y) location, TTarget originalValue, TSource sourceValue);
    }
}