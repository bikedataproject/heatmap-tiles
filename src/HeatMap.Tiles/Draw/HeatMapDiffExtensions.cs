using System;
using System.Collections.Generic;
using HeatMap.Tiles.Diffs;
using NetTopologySuite.Geometries;

namespace HeatMap.Tiles.Draw
{
    /// <summary>
    /// Contains extension methods to draw to an in-memory heatmap.
    /// </summary>
    public static class HeatMapDiffExtensions
    {
        /// <summary>
        /// Adds new geometries to the heat map.
        /// </summary>
        /// <param name="heatMapDiff">The heat map.</param>
        /// <param name="geometries">The geometries.</param>
        /// <param name="cost">The cost.</param>
        /// <param name="includeTile">A function to allow inclusion of only some tiles.</param>
        public static void Draw(this HeatMapDiff heatMapDiff, IEnumerable<Geometry> geometries, uint cost = 1, Func<uint, bool>? includeTile = null)
        {
            foreach (var geometry in geometries)
            {
                heatMapDiff.Draw(geometry, cost, includeTile);
            }
        }
        
        /// <summary>
        /// Adds a new feature to the heat map.
        /// </summary>
        /// <param name="heatMapDiff">The heat map.</param>
        /// <param name="geometry">The geometry.</param>
        /// <param name="cost">The cost.</param>
        /// <param name="includeTile">A function to allow inclusion of only some tiles.</param>
        public static void Draw(this HeatMapDiff heatMapDiff, Geometry geometry, uint cost = 1, Func<uint, bool>? includeTile = null)
        {
            if (!(geometry is LineString ls)) return;

            if (ls.Coordinates.Length == 0) return;
            
            void Draw(long x, long y)
            {
                if (x < 0) return;
                if (y < 0) return;

                heatMapDiff[x, y] += cost;
            }

            var previous = heatMapDiff.ToHeatMapCoordinates(ls.Coordinates[0], includeTile);
            for (var c = 1; c < ls.Coordinates.Length; c++)
            {
                var currentResult = heatMapDiff.ToHeatMapCoordinates(ls.Coordinates[c], includeTile);
                if (currentResult == null) continue;
                var current = currentResult.Value;

                if (previous.HasValue) Bresenhams.Draw(previous.Value.x, previous.Value.y, current.x, current.y, Draw);

                previous = current;
            }
        }
    }
}