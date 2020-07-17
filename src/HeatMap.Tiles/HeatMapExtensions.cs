using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HeatMap.Tiles.Tilers;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.VectorTiles;
using NetTopologySuite.IO.VectorTiles.Tiles;

namespace HeatMap.Tiles
{
    public static class HeatMapExtensions
    {
        /// <summary>
        /// A function to get resolution per zoom level.
        /// </summary>
        /// <param name="zoom">The zoom.</param>
        public delegate uint ToResolution(int zoom);
        
        /// <summary>
        /// Adds a new feature to the heatmap.
        /// </summary>
        /// <param name="heatMap">The heatmap.</param>
        /// <param name="geometry">The geometry.</param>
        /// <param name="cost">The cost.</param>
        public static IEnumerable<(uint x, uint y)> Add(this HeatMap heatMap, Geometry geometry, uint cost = 1)
        {
            if (!(geometry is LineString ls)) yield break;

            foreach (var tileId in ls.Tiles(heatMap.Zoom))
            {
                HeatMapTile? heatMapTile = null;
                var tilePolygon = TileStatic.ToPolygon(heatMap.Zoom, tileId);
                
                var hasWritten = false;
                try
                {
                    foreach (var segment in tilePolygon.Cut(ls))
                    {
                        heatMapTile ??= heatMap.Get(tileId);
                        var written =  heatMapTile.Add(tileId, segment, cost);
                        hasWritten |= written;
                    }
                }
                catch (Exception e)
                {
                                
                }

                if (hasWritten)
                {
                    yield return TileStatic.ToTile(heatMap.Zoom, tileId);
                }
            }
        }
        
        /// <summary>
        /// Adds a new feature to the heatmap.
        /// </summary>
        /// <param name="heatMap">The heatmap.</param>
        /// <param name="feature">The feature.</param>
        /// <param name="cost">The cost.</param>
        public static void Add(this HeatMap heatMap, IFeature feature, uint cost = 1)
        {
            heatMap.Add(feature.Geometry, cost);
        }

        public static IEnumerable<VectorTile> ToVectorTiles(this HeatMap tree, int minZoom, int maxZoom, 
            ToResolution? toResolution = null, Func<(uint x, uint y, int zoom), bool>? tileFilter = null)
        {
            var doneTiles = new HashSet<(uint x, uint y, int zoom)>();
            foreach (var tileId1 in tree.EnumerateTilesAt(maxZoom))
            {
                var z = maxZoom;
                var currentTileId = tileId1;
                while (z >= minZoom)
                {
                    var (x, y) = TileStatic.ToTile(z, currentTileId);
                    var tile = (x, y, z);

                    var tileDone = false;
                    if (z < maxZoom)
                    {
                        if (doneTiles.Contains(tile)) tileDone = true;
                    }
                    if (!tileDone && tileFilter != null)
                    {
                        if (!tileFilter(tile)) tileDone = true;;
                    }

                    if (!tileDone)
                    {
                        var resolution = toResolution?.Invoke(z) ?? 256;
                        if (!tree.TryGetVectorTile(z, currentTileId, resolution, out var vectorTile)) continue;

                        yield return vectorTile;

                        if (z < maxZoom) doneTiles.Add(tile);
                    }

                    var nextZoom = z - 1;
                    currentTileId = TileStatic.ToLocalId((x, y, z).ParentTileFor(nextZoom), nextZoom);
                    z = nextZoom;
                }
            }
            
            
            // for (var z = minZoom; z <= maxZoom; z++)
            // {
            //     var resolution = toResolution?.Invoke(z) ?? 256;
            //     foreach (var tileId in tree.EnumerateTilesAt(z))
            //     {
            //         if (tileFilter != null)
            //         {
            //             var (x, y) = TileStatic.ToTile(z, tileId);
            //             if (!tileFilter((x, y, z))) continue;
            //         }
            //         
            //         if (!tree.TryGetVectorTile(z, tileId, resolution, out var vectorTile)) continue;
            //         
            //         yield return vectorTile;
            //     }
            // }
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

        internal static IEnumerable<uint> EnumerateTilesAt(this HeatMap heatMap, int zoom)
        {
            HashSet<uint> tiles = null;
            foreach (var heatMapTileId in heatMap)
            {
                if (heatMap.Zoom == zoom)
                {
                    yield return heatMapTileId;
                    continue;
                }

                var tile = TileStatic.ToTile(heatMap.Zoom, heatMapTileId);
                if (zoom < heatMap.Zoom)
                {
                    tiles ??= new HashSet<uint>();
                    var parent = TileStatic.ParentTileFor((tile.x, tile.y, heatMap.Zoom), zoom);
                    var parentId  =TileStatic.ToLocalId(parent, zoom);
                    if (tiles.Contains(parentId)) continue;
                    
                    yield return parentId;
                    tiles.Add(parentId);
                }
                else
                {
                    foreach (var subTile in (tile.x, tile.y, heatMap.Zoom).SubTilesFor(zoom))
                    {
                        yield return TileStatic.ToLocalId(subTile, zoom);
                    } 
                }
            }
        }

        internal static bool TryGetVectorTile(this HeatMap heatMap, int zoom, uint tileId, uint resolution,
            out VectorTile vectorTile)
        {
            var tgt = new TileGeometryTransform(zoom, tileId, resolution);

            var tile = TileStatic.ToTile(zoom, tileId);
            vectorTile = new VectorTile()
            {
                TileId = new Tile((int)tile.x, (int)tile.y, zoom).Id,
                Layers = { new Layer()
                {
                    Name = "heatmap"
                }}
            };

            foreach (var (x, y, value) in heatMap.GetValues(zoom, tileId, resolution))
            {
                if (value == 0) continue;
                
                var coordinate = tgt.TransformTo(x, y);
                vectorTile.Layers[0].Features.Add(new Feature(
                    new Point(new Coordinate(coordinate.longitude, coordinate.latitude)), 
                    new AttributesTable {{"cost", value}} ));
            }

            if (vectorTile.Layers[0].Features.Count == 0)
            {
                vectorTile = null;
                return false;
            }

            return true;
        }
        
        public static IEnumerable<(int x, int y, uint value)> GetValues(this HeatMap heatMap, int zoom, uint tileId, uint resolution = 256)
        {
            var (xTile, yTile, scaledResolution) = heatMap.GetTilePosition(zoom, tileId);

            if (scaledResolution < resolution) throw new NotSupportedException("Upscaling of heatmap not supported.");

            
            if (scaledResolution > resolution)
            {
                var factor = scaledResolution / resolution;
                
                for (var x = 0; x < resolution; x++)
                for (var y = 0; y < resolution; y++)
                {
                    var xScaled = x * factor;
                    var yScaled = y * factor;
                    
                    var count = heatMap.SumRange(xTile + xScaled, yTile + yScaled, factor);
                    
                    // var count = 0U;
                    // for (var dx = 0; dx < factor; dx++)
                    // for (var dy = 0; dy < factor; dy++)
                    // {
                    //     count += heatMap[xTile + xScaled + dx, yTile + yScaled + dy];
                    // }
                    if (count != 0) yield return (x, y, count);
                }
                yield break;
            }
            
            for (var x = 0; x < resolution; x++)
            for (var y = 0; y < resolution; y++)
            {
                var count = heatMap[xTile + x, yTile + y];
                if (count != 0) yield return (x, y, count);
            }
        }

        internal static (long x, long y, long resolution) GetTilePosition(this HeatMap heatMap, int zoom, uint tileId)
        {
            if (zoom > heatMap.Zoom) throw new NotSupportedException("Upscaling of heatmap not supported.");
            
            uint factor = 1;
            var tile = TileStatic.ToTile(zoom, tileId);
            if (zoom < heatMap.Zoom)
            {
                factor = (uint)(1 << (int)(heatMap.Zoom - zoom));
                tile = (tile.x * factor, tile.y * factor);
            }

            return ((long)tile.x * heatMap.Resolution, (long)tile.y * heatMap.Resolution,  heatMap.Resolution * factor);
        }
    }
}