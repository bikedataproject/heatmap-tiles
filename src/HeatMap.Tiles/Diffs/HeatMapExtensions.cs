using System;
using System.Collections.Generic;

namespace HeatMap.Tiles.Diffs
{
    /// <summary>
    /// Heatmap extensions to apply diffs.
    /// </summary>
    public static class HeatMapExtensions
    {
        /// <summary>
        /// Applies the given diff by writing it to the heat map.
        /// </summary>
        /// <param name="heatMap">The heat map.</param>
        /// <param name="diff">The diff.</param>
        /// <param name="minZoom">The minimum zoom level.</param>
        public static IEnumerable<(uint x, uint y, int z)> ApplyDiff(this HeatMap<uint> heatMap, HeatMapDiff diff, int minZoom = 0)
        {
            while (true)
            {
                // create next diff when the minimum zoom has been been reached yet.
                HeatMapDiff? nextDiff = null;
                if (diff.Zoom > minZoom)
                {
                    nextDiff = diff.CreateOneLevelUp();
                }

                // write all data in this diff.
                foreach (var tileId in diff.EnumerateTilesAt(diff.Zoom))
                {
                    heatMap.ApplyDiff(diff, tileId, nextDiff);

                    var (x, y) = TileStatic.ToTile(diff.Zoom, tileId);
                    yield return (x, y, diff.Zoom);
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
        private static void ApplyDiff(this HeatMap<uint> heatMap, HeatMapDiff diff, uint tileId, HeatMapDiff? nextDiff)
        {
            if (nextDiff == null)
            {
                // get tile coordinates.
                var tile = TileStatic.ToTile(diff.Zoom, tileId);

                // get heat map tile.
                var heatMapTile = heatMap[tile.x, tile.y, diff.Zoom];
                if (heatMapTile.Resolution != diff.Resolution)
                    throw new NotSupportedException("Resolutions don't match.");

                // get the top left of the tile in the diff.
                var (xTile, yTile) = diff.GetTilePosition(tileId);

                // get all data for the given tile and add it to the heat map.
                // downscale while doing this by writing to the next diff.
                for (var i = 0; i < heatMapTile.Resolution; i++)
                for (var j = 0; j < heatMapTile.Resolution; j++)
                {
                    var value = diff[xTile + i, yTile + j];
                    if (value == 0) continue;

                    var existing = (long)heatMapTile[i, j];
                    existing += value;
                    if (existing > uint.MaxValue) existing = uint.MaxValue;

                    heatMapTile[i, j] += (uint) existing;
                }
            }
            else
            {
                if (diff.Zoom - 1 != nextDiff.Zoom)
                    throw new NotSupportedException("Only steps of one zoom level are supported.");

                // get tile coordinates.
                var tile = TileStatic.ToTile(diff.Zoom, tileId);

                // get heat map tile.
                var heatMapTile = heatMap[tile.x, tile.y, diff.Zoom];
                if (heatMapTile.Resolution != diff.Resolution)
                    throw new NotSupportedException("Resolutions don't match.");

                // get parent tile.
                var parentTile = (tile.x, tile.y, diff.Zoom).ParentTileFor(nextDiff.Zoom);
                var parentTileId = TileStatic.ToLocalId(parentTile, nextDiff.Zoom);

                // get the top left of the tile in the diff.
                var (xTile, yTile) = diff.GetTilePosition(tileId);

                // get the top left of the tile in the next diff.
                var (xTileNext, yTileNext) = nextDiff.GetTilePosition(parentTileId);
                var scale = diff.Resolution / nextDiff.Resolution * 2;
                xTileNext += (tile.x - (parentTile.x * 2)) * (nextDiff.Resolution / scale);
                yTileNext += (tile.y - (parentTile.y * 2)) * (nextDiff.Resolution / scale);

                // get all data for the given tile and add it to the heat map.
                // downscale while doing this by writing to the next diff.
                for (var i = 0; i < heatMapTile.Resolution; i++)
                for (var j = 0; j < heatMapTile.Resolution; j++)
                {
                    var value = diff[xTile + i, yTile + j];
                    if (value == 0) continue;

                    var existing = (long) heatMapTile[i, j];
                    existing += value;
                    if (existing > uint.MaxValue) existing = uint.MaxValue;

                    heatMapTile[i, j] = (uint) existing;

                    var iScaled = i / scale;
                    var jScaled = j / scale;

                    existing = (long) nextDiff[xTileNext + iScaled, yTileNext + jScaled];
                    existing += value;
                    if (existing > uint.MaxValue) existing = uint.MaxValue;

                    nextDiff[xTileNext + iScaled, yTileNext + jScaled] = (uint) existing;
                }
            }
            
            heatMap.FlushAndUnload();
        }
    }
}