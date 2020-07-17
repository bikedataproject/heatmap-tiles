using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HeatMap.Tiles.Tilers;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.VectorTiles;
using NetTopologySuite.IO.VectorTiles.Tiles;

namespace HeatMap.Tiles.Diffs
{
    public static class HeatMapDiffExtensions
    {
        /// <summary>
        /// Adds a new feature to the heatmap.
        /// </summary>
        /// <param name="heatMapDiff">The heatmap.</param>
        /// <param name="geometry">The geometry.</param>
        /// <param name="cost">The cost.</param>
        public static void Add(this HeatMapDiff heatMapDiff, Geometry geometry, uint cost = 1)
        {
            if (!(geometry is LineString ls)) return;

            foreach (var tileId in ls.Tiles(heatMapDiff.Zoom))
            {
                var tilePolygon = TileStatic.ToPolygon(heatMapDiff.Zoom, tileId);
                
                try
                {
                    foreach (var segment in tilePolygon.Cut(ls))
                    {
                        heatMapDiff.Add(tileId, segment, cost);
                    }
                }
                catch (Exception e)
                {
                                
                }
            }
        }

        internal static (long x, long y) GetTilePosition(this HeatMapDiff diff, uint tileId)
        {
            var (x, y) = TileStatic.ToTile(diff.Zoom, tileId);
            return (x * diff.Resolution, y * diff.Resolution);
        }
        
        /// <summary>
        /// Adds a new feature to the heatmap.
        /// </summary>
        /// <param name="heatMapDiff">The heatmap.</param>
        /// <param name="feature">The feature.</param>
        /// <param name="cost">The cost.</param>
        public static void Add(this HeatMapDiff heatMapDiff, IFeature feature, uint cost = 1)
        {
            heatMapDiff.Add(feature.Geometry, cost);
        }

        public static Func<(uint x, uint y, int zoom), bool> ToTileFilter(this HashSet<(uint x, uint y)> tiles,
            int zoom)
        {
            bool IsModified((uint x, uint y, int zoom) tile)
            {
                if (tile.zoom == zoom)
                {
                    return tiles.Contains((tile.x, tile.y));
                }

                if (tile.zoom < zoom)
                {
                    var subtiles = TileStatic.SubTilesFor(tile, zoom);
                    foreach (var subtile in subtiles)
                    {
                        if (tiles.Contains(subtile)) return true;
                    }
                }

                return false;
            }

            return IsModified;
        }

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

        // internal static bool TryGetVectorTile(this HeatMapDiff heatMapDiff, int zoom, uint tileId, uint resolution,
        //     out VectorTile vectorTile)
        // {
        //     var tgt = new TileGeometryTransform(zoom, tileId, resolution);
        //
        //     var tile = TileStatic.ToTile(zoom, tileId);
        //     vectorTile = new VectorTile()
        //     {
        //         TileId = new Tile((int)tile.x, (int)tile.y, zoom).Id,
        //         Layers = { new Layer()
        //         {
        //             Name = "heatmap"
        //         }}
        //     };
        //
        //     foreach (var (x, y, value) in heatMapDiff.GetValues(zoom, tileId, resolution))
        //     {
        //         if (value == 0) continue;
        //         
        //         var coordinate = tgt.TransformTo(x, y);
        //         vectorTile.Layers[0].Features.Add(new Feature(
        //             new Point(new Coordinate(coordinate.longitude, coordinate.latitude)), 
        //             new AttributesTable {{"cost", value}} ));
        //     }
        //
        //     if (vectorTile.Layers[0].Features.Count == 0)
        //     {
        //         vectorTile = null;
        //         return false;
        //     }
        //
        //     return true;
        // }
        
        // public static IEnumerable<(int x, int y, uint value)> GetValues(this HeatMapDiff heatMapDiff, int zoom, uint tileId, uint resolution = 256)
        // {
        //     var (xTile, yTile, scaledResolution) = heatMapDiff.GetTilePosition(zoom, tileId);
        //
        //     if (scaledResolution < resolution) throw new NotSupportedException("Upscaling of heatmap not supported.");
        //
        //     
        //     if (scaledResolution > resolution)
        //     {
        //         var factor = scaledResolution / resolution;
        //         
        //         for (var x = 0; x < resolution; x++)
        //         for (var y = 0; y < resolution; y++)
        //         {
        //             var xScaled = x * factor;
        //             var yScaled = y * factor;
        //             
        //             var count = heatMapDiff.SumRange(xTile + xScaled, yTile + yScaled, factor);
        //             
        //             // var count = 0U;
        //             // for (var dx = 0; dx < factor; dx++)
        //             // for (var dy = 0; dy < factor; dy++)
        //             // {
        //             //     count += heatMap[xTile + xScaled + dx, yTile + yScaled + dy];
        //             // }
        //             if (count != 0) yield return (x, y, count);
        //         }
        //         yield break;
        //     }
        //     
        //     for (var x = 0; x < resolution; x++)
        //     for (var y = 0; y < resolution; y++)
        //     {
        //         var count = heatMapDiff[xTile + x, yTile + y];
        //         if (count != 0) yield return (x, y, count);
        //     }
        // }

        // internal static (long x, long y, long resolution) GetTilePosition(this HeatMapDiff heatMapDiff, int zoom, uint tileId)
        // {
        //     if (zoom > heatMapDiff.Zoom) throw new NotSupportedException("Upscaling of heatmap not supported.");
        //     
        //     uint factor = 1;
        //     var tile = TileStatic.ToTile(zoom, tileId);
        //     if (zoom < heatMapDiff.Zoom)
        //     {
        //         factor = (uint)(1 << (int)(heatMapDiff.Zoom - zoom));
        //         tile = (tile.x * factor, tile.y * factor);
        //     }
        //
        //     return ((long)tile.x * heatMapDiff.Resolution, (long)tile.y * heatMapDiff.Resolution,  heatMapDiff.Resolution * factor);
        // }
    }
}