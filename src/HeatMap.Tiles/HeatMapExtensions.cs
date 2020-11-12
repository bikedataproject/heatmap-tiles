using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HeatMap.Tiles.Diffs;
using NetTopologySuite.Geometries;

[assembly:InternalsVisibleTo("HeatMap.Tiles.Test")]
[assembly:InternalsVisibleTo("HeatMap.Tiles.Test.Functional")]
namespace HeatMap.Tiles
{
    /// <summary>
    /// Contains extensions methods for the heat map.
    /// </summary>
    public static class HeatMapExtensions
    {
        /// <summary>
        /// Copies the data from the given heat map to the target heat map for the given tiles.
        /// </summary>
        /// <param name="heapMap">The source heat map.</param>
        /// <param name="target">The target heat map.</param>
        /// <param name="tiles">The tiles to copy for.</param>
        /// <param name="translate">A function to translate values.</param>
        public static void CopyTilesTo(this HeatMap<uint> heapMap, HeatMap<uint> target, IEnumerable<(uint x, uint y, int z)> tiles,
            Delegates.ValuePerTileFunc<uint, uint>? translate = null)
        {
            foreach (var tile in tiles)
            {
                if (!heapMap.TryGetTile(tile, out var sourceTile)) continue;

                var targetTile = target[tile.x, tile.y, tile.z];
                if (sourceTile.Resolution != targetTile.Resolution)
                    throw new NotSupportedException("Resolutions don't match.");


                foreach (var (x, y, v) in sourceTile.GetValues())
                {
                    var value = v;
                    if (translate != null)
                    {
                        value = translate(tile, (x, y), value);
                    }

                    if (value > 0)
                    {
                        targetTile[x, y] = value;
                    }
                }
            }
        }
        
        /// <summary>
        /// Copies the data from the given heat map to the target heat map for the given tiles.
        /// </summary>
        /// <param name="heapMap">The source heat map.</param>
        /// <param name="target">The target heat map.</param>
        /// <param name="tiles">The tiles to copy for.</param>
        /// <param name="translate">A function to translate values.</param>
        public static void CopyTilesTo<TSource, TTarget>(this HeatMap<TSource> heapMap, HeatMap<TTarget> target, IEnumerable<(uint x, uint y, int z)> tiles,
            Delegates.ValuePerTileFunc<TSource, TTarget> translate)
                where TSource: struct
                where TTarget: struct
        {
            foreach (var tile in tiles)
            {
                if (!heapMap.TryGetTile(tile, out var sourceTile)) continue;

                var targetTile = target[tile.x, tile.y, tile.z];
                if (sourceTile.Resolution != targetTile.Resolution)
                    throw new NotSupportedException("Resolutions don't match.");

                foreach (var (x, y, v) in sourceTile.GetValues())
                {
                    var value = translate(tile, (x, y), v);
                    if (value.Equals(default(TTarget))) continue;

                    targetTile[x, y] = value;
                }
            }
        }

        /// <summary>
        /// Copies the data from the given heat map to the target heat map for the given tiles.
        /// </summary>
        /// <param name="heapMap">The source heat map.</param>
        /// <param name="target">The target heat map.</param>
        /// <param name="tiles">The tiles to copy for.</param>
        /// <param name="translate">A function to translate values.</param>
        public static void AddTilesTo(this HeatMap<uint> heapMap, HeatMap<uint> target, IEnumerable<(uint x, uint y, int z)> tiles,
            Delegates.ValuePerTileFunc<uint, uint>? translate = null)
        {
            foreach (var tile in tiles)
            {
                if (!heapMap.TryGetTile(tile, out var sourceTile)) continue;

                var targetTile = target[tile.x, tile.y, tile.z];
                if (sourceTile.Resolution != targetTile.Resolution)
                    throw new NotSupportedException("Resolutions don't match.");

                foreach (var (x, y, v) in sourceTile.GetValues())
                {
                    var value = v;
                    if (translate != null)
                    {
                        value = translate(tile, (x, y), value);
                    }

                    if (value > 0)
                    {
                        targetTile[x, y] += value;
                    }
                }
            }
        }

        /// <summary>
        /// Copies the data from the given heat map to the target heat map for the given tiles.
        /// </summary>
        /// <param name="heapMap">The source heat map.</param>
        /// <param name="target">The target heat map.</param>
        /// <param name="tiles">The tiles to copy for.</param>
        /// <param name="translate">A function to translate values.</param>
        public static void AddTilesTo<TSource, TTarget>(this HeatMap<TSource> heapMap, HeatMap<TTarget> target, IEnumerable<(uint x, uint y, int z)> tiles,
            Delegates.AddPerTileFunc<TSource, TTarget> translate)
            where TSource : struct
            where TTarget : struct
        {
            foreach (var tile in tiles)
            {
                if (!heapMap.TryGetTile(tile, out var sourceTile)) continue;

                var targetTile = target[tile.x, tile.y, tile.z];
                if (sourceTile.Resolution != targetTile.Resolution)
                    throw new NotSupportedException("Resolutions don't match.");

                foreach (var (x, y, v) in sourceTile.GetValues())
                {
                    targetTile[x, y] = 
                        translate(tile, (x, y), targetTile[x, y], v);
                }
            }
        }

        /// <summary>
        /// Rewrite the tile tree for the given roots.
        /// </summary>
        /// <param name="heatMap">The heat map.</param>
        /// <param name="tiles">The modified tiles.</param>
        public static IEnumerable<(uint x, uint y, int z)> RebuildParentTileTree(this HeatMap<ulong> heatMap, IEnumerable<(uint x, uint y, int z)> tiles)
        {
            while (true)
            {
                var parentQueue = new HashSet<(uint x, uint y, int z)>();

                foreach (var tile in tiles)
                {
                    if (tile.z == 0) continue;

                    var parent = tile.ParentTileFor(tile.z - 1);
                    parentQueue.Add((parent.x, parent.y, tile.z - 1));
                }

                if (parentQueue.Count == 0) yield break;

                foreach (var tile in parentQueue)
                {
                    heatMap.RebuildTile(tile);
                    yield return tile;
                }

                tiles = parentQueue;
            }
        }

        /// <summary>
        /// Rebuilds the given tile from the 4 sub-tiles right below.
        /// </summary>
        /// <param name="heatMap">The heat map.</param>
        /// <param name="tile">The tile.</param>
        /// <remarks>If there are no sub-tiles found the tile is removed.</remarks>
        public static void RebuildTile(this HeatMap<ulong> heatMap, (uint x, uint y, int z) tile)
        {
            heatMap.TryRemoveTile(tile);
            
            var subTileRange = tile.SubTileRangeFor(tile.z + 1);

            var xMin = subTileRange.topLeft.x;
            var yMin = subTileRange.topLeft.y;

            for (uint xOffset = 0; xOffset < 2; xOffset++)
            for (uint yOffset = 0; yOffset < 2; yOffset++)
            {
                var subTile = (xMin + xOffset, yMin + yOffset, tile.z + 1);
                if (!heatMap.TryGetTile(subTile, out var heatMapSubTile)) continue;
                
                var left = (heatMap.Resolution / 2) * xOffset;
                var top = (heatMap.Resolution / 2) * yOffset;
                var scale = heatMap.Resolution / heatMapSubTile.Resolution * 2;

                HeatMapTile<ulong>? heatMapTile = null;
                foreach (var val in heatMapSubTile.GetValues())
                {
                    // has to be here, only for tiles that exist!
                    heatMapTile ??= heatMap[tile.x, tile.y, tile.z];
                    
                    // calculate parent tile coordinates.
                    var x = (int)(left + (val.x / scale));
                    var y = (int)(top + (val.y / scale));

                    heatMapTile[x, y] += val.value;
                }
            }
            
            heatMap.FlushAndUnload();
        }
    }
}