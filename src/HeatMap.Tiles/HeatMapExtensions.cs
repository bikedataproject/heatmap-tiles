using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HeatMap.Tiles.Diffs;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.VectorTiles;
using NetTopologySuite.IO.VectorTiles.Tiles;

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
        /// A function to get resolution per zoom level.
        /// </summary>
        /// <param name="zoom">The zoom.</param>
        public delegate uint ToResolution(int zoom);

        /// <summary>
        /// Adds the given geometries to the heat map.
        /// </summary>
        /// <param name="heatMap">The heat map.</param>
        /// <param name="geometries">The geometries.</param>
        /// <param name="minZoom">The minimum zoom level.</param>
        /// <param name="toResolution">The resolution function.</param>
        /// <param name="zoom">The zoom level.</param>
        /// <param name="resolution">The resolution.</param>
        public static IEnumerable<(uint x, uint y, int z)> Add(this HeatMap heatMap, IEnumerable<Geometry> geometries, int zoom = 14, uint resolution = 1024, 
            ToResolution toResolution = null, int minZoom = 0)
        {
            var heatMapDiff = new HeatMapDiff(zoom, resolution);
            foreach (var geometry in geometries)
            {
                heatMapDiff.Add(geometry);
            }

            return heatMap.ApplyDiff(heatMapDiff, minZoom, toResolution);
        }

        /// <summary>
        /// Applies the given diff by writing it to the heat map.
        /// </summary>
        /// <param name="heatMap">The heat map.</param>
        /// <param name="diff">The diff.</param>
        /// <param name="minZoom">The minimum zoom level.</param>
        /// <param name="toResolution">The resolution function.</param>
        public static IEnumerable<(uint x, uint y, int z)> ApplyDiff(this HeatMap heatMap, HeatMapDiff diff, int minZoom = 0,
            ToResolution toResolution = null)
        {
            while (true)
            {
                // create next diff when the minimum zoom has been been reached yet.
                HeatMapDiff nextDiff = null;
                if (diff.Zoom > minZoom)
                {
                    nextDiff = diff.CreateOneLevelUp(toResolution);
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
        private static void ApplyDiff(this HeatMap heatMap, HeatMapDiff diff, uint tileId, HeatMapDiff nextDiff)
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
                if (diff.Zoom - 1 != nextDiff.Zoom) throw new NotSupportedException("Only steps of one zoom level are supported.");
                
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

                    var existing = (long)heatMapTile[i, j];
                    existing += value;
                    if (existing > uint.MaxValue) existing = uint.MaxValue;

                    heatMapTile[i, j] = (uint) existing;

                    var iScaled = i / scale;
                    var jScaled = j / scale;
                    
                    existing = (long)nextDiff[xTileNext + iScaled, yTileNext + jScaled];
                    existing += value;
                    if (existing > uint.MaxValue) existing = uint.MaxValue;
                    
                    nextDiff[xTileNext + iScaled, yTileNext + jScaled] = (uint) existing;
                }
            }
        }
        
        public static IEnumerable<VectorTile> ToVectorTiles(this HeatMap heatMap, 
            IEnumerable<(uint x, uint y, int z)> tiles)
        {
            foreach (var tile in tiles)
            {
                var vt = heatMap.ToVectorTile(tile);
                if (vt == null) continue;

                yield return vt;
            }
        }
        
        public static VectorTile ToVectorTile(this HeatMap heatMap, 
            (uint x, uint y, int z) tile)
        {
            if (!heatMap.TryGetTile(tile, out var heatMapTile)) return null;

            var zoom = tile.z;
            var tileId = TileStatic.ToLocalId(tile.x, tile.y, zoom);
            var tgt = new TileGeometryTransform(zoom, tileId, heatMapTile.Resolution);
            
            var tileCoordinates = TileStatic.ToTile(zoom, tileId);
            var vectorTile = new VectorTile()
            {
                TileId = new Tile((int)tile.x, (int)tile.y, zoom).Id,
                Layers = { new Layer()
                {
                    Name = "heatmap"
                }}
            };

            foreach (var (x, y, value) in heatMapTile.GetValues())
            {
                var coordinate = tgt.TransformTo(x, y);
                vectorTile.Layers[0].Features.Add(new Feature(
                    new Point(new Coordinate(coordinate.longitude, coordinate.latitude)), 
                    new AttributesTable {{"cost", value}} ));
            }
            
            if (vectorTile.Layers[0].Features.Count == 0)
            {
                return null;
            }

            return vectorTile;
        }
    }
}