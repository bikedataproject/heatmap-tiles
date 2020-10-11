using System.Collections.Generic;
using HeatMap.Tiles.Diffs;
using NetTopologySuite.Geometries;

namespace HeatMap.Tiles.Draw
{
    /// <summary>
    /// Contains extension methods to draw geometries.
    /// </summary>
    public static class HeatMapExtensions
    {
        /// <summary>
        /// Adds the given geometries to the heat map.
        /// </summary>
        /// <param name="heatMap">The heat map.</param>
        /// <param name="geometries">The geometries.</param>
        /// <param name="minZoom">The minimum zoom level.</param>
        /// <param name="zoom">The zoom level.</param>
        /// <param name="resolution">The resolution.</param>
        public static IEnumerable<(uint x, uint y, int z)> Draw(this HeatMap<uint> heatMap, IEnumerable<Geometry> geometries, int zoom = 14, uint resolution = 1024, 
            int minZoom = 0)
        {
            var heatMapDiff = new HeatMapDiff(zoom, resolution);
            foreach (var geometry in geometries)
            {
                heatMapDiff.Draw(geometry);
            }

            return heatMap.ApplyDiff(heatMapDiff, minZoom);
        }
    }
}