using System;
using HeatMap.Tiles.Diffs;

namespace HeatMap.Tiles
{
    /// <summary>
    /// Contains extensions methods for the heat map.
    /// </summary>
    public static class HeatMapExtensions
    {
        /// <summary>
        /// A function to get resolution per zoom level.
        /// </summary>
        /// <param name="zoom">The zoom.</param>
        public delegate uint ToResolution(int zoom);
        
        /// <summary>
        /// Applies the given diff by writing it to the heat map.
        /// </summary>
        /// <param name="heatMap">The heat map.</param>
        /// <param name="diff">The diff.</param>
        /// <param name="minZoom">The minimum zoom level.</param>
        /// <param name="toResolution">The resolution function.</param>
        public static void ApplyDiff(this HeatMap heatMap, HeatMapDiff diff, int minZoom = 0,
            ToResolution toResolution = null)
        {
            while (true)
            {
                // create next diff when the minimum zoom has been been reached yet.
                HeatMapDiff nextDiff = null;
                if (diff.Zoom > minZoom)
                {
                    nextDiff = new HeatMapDiff(diff.Zoom - 1, toResolution?.Invoke(diff.Zoom - 1) ?? 1024);
                }

                // write all data in this diff.
                foreach (var tile in diff.EnumerateTilesAt(diff.Zoom))
                {
                    heatMap.ApplyDiff(diff, tile, nextDiff);
                }
                
                if (nextDiff == null) break;
                diff = nextDiff;
            }
        }

        /// <summary>
        /// Adds the data in the diff to the given heat map and downscales the same data to the next diff.
        /// </summary>
        /// <param name="heatMap">The heat map to add to.</param>
        /// <param name="diff">The diff to apply.</param>
        /// <param name="tileId">The tile in the diff to apply.</param>
        /// <param name="nextDiff">The next diff to downscale data into.</param>
        private static void ApplyDiff(this HeatMap heatMap, HeatMapDiff diff, uint tileId, HeatMapDiff nextDiff)
        {
            if (diff.Zoom - 1 != nextDiff.Zoom) throw new NotSupportedException("Only steps of one zoom level are supported.");

            // get tile coordinates.
            var tile = TileStatic.ToTile(diff.Zoom, tileId);
            
            // get heat map tile.
            var heatMapTile = heatMap[tile.x, tile.y, diff.Zoom];
            if (heatMapTile.Resolution != diff.Resolution) throw new NotSupportedException("Resolutions don't match.");
            
            // get parent tile.
            var parentTile = (tile.x, tile.y, diff.Zoom).ParentTileFor(nextDiff.Zoom);
            var parentTileId = TileStatic.ToLocalId(parentTile, nextDiff.Zoom);
            
            // get the top left of the tile in the diff.
            var (xTile, yTile, _) = diff.GetTilePosition(diff.Zoom, tileId);
            
            // get the top left of the tile in the next diff.
            var (xTileNext, yTileNext, _) = nextDiff.GetTilePosition(nextDiff.Zoom, parentTileId);
            xTileNext += (tile.x - (parentTile.x * 2)) * nextDiff.Resolution;
            yTileNext += (tile.y - (parentTile.y * 2)) * nextDiff.Resolution;
            
            // get all data for the given tile and add it to the heat map.
            // downscale while doing this by writing to the next diff.
            var scale = diff.Resolution / nextDiff.Resolution * 2;
            for (var i= 0; i < heatMapTile.Resolution; i++)
            for (var j= 0; j < heatMapTile.Resolution; j++)
            {
                var value = diff[xTile + i, yTile + j];
                if (value == 0) continue;

                heatMapTile[i, j] += value;

                var iScaled = i / scale;
                var jScaled = j / scale;
                nextDiff[xTileNext + iScaled, yTileNext + jScaled] += value;
            }
        }
    }
}