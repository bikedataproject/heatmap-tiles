using System;
using System.Collections.Generic;
using HeatMap.Tiles.Draw;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;

namespace HeatMap.Tiles.Diffs
{
    /// <summary>
    /// Contains helper extension methods.
    /// </summary>
    public static class HeatMapDiffExtensions
    {
        internal static (long x, long y)? ToHeatMapCoordinates(this HeatMapDiff heatMapDiff, Coordinate coordinate, 
            Func<uint, bool>? includeTile = null)
        {
            var localTile = TileStatic.ToLocalTileCoordinates(heatMapDiff.Zoom, (coordinate.X, coordinate.Y),
                (int)heatMapDiff.Resolution);
            if (includeTile != null && !includeTile(localTile.tileId)) return null;
            
            var tile = TileStatic.ToTile(heatMapDiff.Zoom, localTile.tileId);
            return (tile.x * heatMapDiff.Resolution + localTile.x,
                tile.y * heatMapDiff.Resolution + localTile.y);
        }

        internal static (long x, long y) GetTilePosition(this HeatMapDiff diff, uint tileId)
        {
            var (x, y) = TileStatic.ToTile(diff.Zoom, tileId);
            return (x * diff.Resolution, y * diff.Resolution);
        }

        // public static Func<(uint x, uint y, int zoom), bool> ToTileFilter(this HashSet<(uint x, uint y)> tiles,
        //     int zoom)
        // {
        //     bool IsModified((uint x, uint y, int zoom) tile)
        //     {
        //         if (tile.zoom == zoom)
        //         {
        //             return tiles.Contains((tile.x, tile.y));
        //         }
        //
        //         if (tile.zoom < zoom)
        //         {
        //             var subtiles = TileStatic.SubTilesFor(tile, zoom);
        //             foreach (var subtile in subtiles)
        //             {
        //                 if (tiles.Contains(subtile)) return true;
        //             }
        //         }
        //
        //         return false;
        //     }
        //
        //     return IsModified;
        // }

        internal static IEnumerable<uint> EnumerateTilesAt(this HeatMapDiff heatMapDiff, int zoom)
        {
            HashSet<uint> tiles = null;
            foreach (var heatMapTileId in heatMapDiff)
            {
                if (heatMapDiff.Zoom == zoom)
                {
                    yield return heatMapTileId;
                    continue;
                }

                var tile = TileStatic.ToTile(heatMapDiff.Zoom, heatMapTileId);
                if (zoom < heatMapDiff.Zoom)
                {
                    tiles ??= new HashSet<uint>();
                    var parent = TileStatic.ParentTileFor((tile.x, tile.y, heatMapDiff.Zoom), zoom);
                    var parentId  =TileStatic.ToLocalId(parent, zoom);
                    if (tiles.Contains(parentId)) continue;
                    
                    yield return parentId;
                    tiles.Add(parentId);
                }
                else
                {
                    foreach (var subTile in (tile.x, tile.y, heatMapDiff.Zoom).SubTilesFor(zoom))
                    {
                        yield return TileStatic.ToLocalId(subTile, zoom);
                    } 
                }
            }
        }
    }
}