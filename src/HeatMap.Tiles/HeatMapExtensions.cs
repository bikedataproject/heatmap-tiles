using System;
using System.Collections.Generic;
using HeatMap.Tiles.Tilers;
using HeatMap.Tiles.Tiles;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.VectorTiles;

namespace HeatMap.Tiles
{
    public static class HeatMapExtensions
    {
        /// <summary>
        /// A function to get resolution per zoom level.
        /// </summary>
        /// <param name="zoom">The zoom.</param>
        public delegate uint ToResolution(uint zoom);
        
        /// <summary>
        /// Adds a new feature to the heatmap.
        /// </summary>
        /// <param name="heatMap">The heatmap.</param>
        /// <param name="feature">The feature.</param>
        /// <param name="cost">The cost.</param>
        public static void Add(this HeatMap heatMap, IFeature feature, uint cost = 1)
        {
            if (!(feature.Geometry is LineString ls)) return;
            
            foreach (var tileId in ls.Tiles(12))
            {
                var tile = new Tile(tileId);
                var heatmapTile = heatMap.Get(tileId);
                var tilePolygon = tile.ToPolygon();
                try
                {
                    foreach (var segment in tilePolygon.Cut(ls))
                    {
                        heatmapTile.Add(segment, cost);
                    }
                }
                catch (Exception e)
                {
                                
                }
            }
        }

        public static IEnumerable<VectorTile> ToVectorTiles(this HeatMap tree, uint minZoom, uint maxZoom, 
            ToResolution? toResolution = null)
        {
            for (var z = minZoom; z <= maxZoom; z++)
            {
                var resolution = toResolution?.Invoke(z) ?? 256;
                foreach (var tile in tree.EnumerateTilesAt(z))
                {
                    yield return tree.GetVectorTile(tile, resolution);
                }
            }
        }

        internal static IEnumerable<ulong> EnumerateTilesAt(this HeatMap heatMap, uint zoom)
        {
            if (heatMap.Zoom < zoom) throw new NotSupportedException("Upscaling of heatmap not supported."); 
            foreach (var heatMapTile in heatMap)
            {
                if (heatMap.Zoom == zoom)
                {
                    yield return new Tile((int)heatMapTile.x, (int)heatMapTile.y, (int)zoom).Id;
                }
                
                var tile = new Tile((int)heatMapTile.x, (int)heatMapTile.y, (int)heatMap.Zoom);
                foreach (var subTile in tile.GetSubTiles((int)zoom))
                {
                    yield return subTile.Id;
                }
            }
        }

        internal static VectorTile GetVectorTile(this HeatMap heatMap, ulong tileId, uint resolution)
        {
            var tile = new Tile(tileId);
            var tgt = new TileGeometryTransform(tile, resolution);
                
            var vectorTile = new VectorTile()
            {
                TileId = tileId,
                Layers = { new Layer()
                {
                    Name = "heatmap"
                }}
            };

            foreach (var (x, y, value) in heatMap.GetValues(tileId, resolution))
            {
                if (value == 0) continue;
                
                var coordinate = tgt.TransformTo(x, y);
                vectorTile.Layers[0].Features.Add(new Feature(
                    new Point(new Coordinate(coordinate.longitude, coordinate.latitude)), 
                    new AttributesTable {{"cost", value}} ));
            }

            return vectorTile;
        }
        
        public static IEnumerable<(int x, int y, uint value)> GetValues(this HeatMap heatMap, ulong tileId, uint resolution = 256)
        {
            var (xTile, yTile, scaledResolution) = heatMap.GetTilePosition(tileId);

            if (scaledResolution < resolution) throw new NotSupportedException("Upscaling of heatmap not supported.");

            if (scaledResolution > resolution)
            {
                var factor = scaledResolution / resolution;
                for (var x = 0; x < resolution; x++)
                for (var y = 0; y < resolution; y++)
                {
                    var xScaled = x * factor;
                    var yScaled = y * factor;
                    var count = 0U;
                    for (var dx = 0; dx < factor; dx++)
                    for (var dy = 0; dy < factor; dy++)
                    {
                        count += heatMap[xTile + xScaled + dx, yTile + yScaled + dy];
                    }
                    if (count != 0) yield return (x, y, count);
                }
            }
            
            for (var x = 0; x < resolution; x++)
            for (var y = 0; y < resolution; y++)
            {
                var count = heatMap[xTile + x, yTile + y];
                if (count != 0) yield return (x, y, count);
            }
        }

        internal static (long x, long y, long resolution) GetTilePosition(this HeatMap heatMap, ulong tileId)
        {
            var tile = new Tile(tileId);
            var factor = 1;
            if (tile.Zoom > heatMap.Zoom) throw new NotSupportedException("Upscaling of heatmap not supported.");
            if (tile.Zoom < heatMap.Zoom)
            {
                factor = 1 << (int)(heatMap.Zoom - tile.Zoom);
            }

            var resolution = heatMap.Resolution * factor;
            return ((long)tile.X * resolution, (long)tile.Y * resolution, resolution);
        }
    }
}