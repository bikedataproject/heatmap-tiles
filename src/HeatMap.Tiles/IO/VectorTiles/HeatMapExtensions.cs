using System;
using System.Collections.Generic;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.VectorTiles;
using NetTopologySuite.IO.VectorTiles.Tiles;

namespace HeatMap.Tiles.IO.VectorTiles
{
    /// <summary>
    /// Contains extension methods to write heat maps to vector tiles.
    /// </summary>
    public static class HeatMapExtensions
    {
        /// <summary>
        /// Converts the given tiles from the heat map to vector tiles.
        /// </summary>
        /// <param name="heatMap">The heat map.</param>
        /// <param name="tiles">The tiles.</param>
        /// <param name="getAttributes">Gets the attribute collection.</param>
        /// <returns>The vector tiles.</returns>
        public static IEnumerable<VectorTile> ToVectorTiles<THeatMap>(this HeatMap<THeatMap> heatMap,
            IEnumerable<(uint x, uint y, int z)> tiles, Func<(uint x, uint y, int z), THeatMap, AttributesTable?> getAttributes)
            where THeatMap : struct
        {
            foreach (var tile in tiles)
            {
                var vt = heatMap.ToVectorTile(tile, x => getAttributes(tile, x));
                if (vt == null) continue;

                yield return vt;
            }
        }

        /// <summary>
        /// Converts a single tile from the heat map to a vector tile.
        /// </summary>
        /// <param name="heatMap">The heat map.</param>
        /// <param name="tile">The tile.</param>
        /// <param name="getValue">Gets the heat map value.</param>
        /// <returns>The vector tile.</returns>
        public static VectorTile? ToVectorTile<THeatMap>(this HeatMap<THeatMap> heatMap, 
            (uint x, uint y, int z) tile, Func<THeatMap, AttributesTable?> getValue)
            where THeatMap : struct
        {
            if (!heatMap.TryGetTile(tile, out var heatMapTile)) return null;

            var zoom = tile.z;
            var tileId = TileStatic.ToLocalId(tile.x, tile.y, zoom);
            var tgt = new TileGeometryTransform(zoom, tileId, heatMapTile.Resolution);
            
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
                var attributesTable = getValue(value);
                if (attributesTable == null) continue;
                
                var (longitude, latitude) = tgt.TransformTo(x, y);
                vectorTile.Layers[0].Features.Add(new Feature(
                    new Point(new Coordinate(longitude, latitude)), 
                    attributesTable));
            }
            
            if (vectorTile.Layers[0].Features.Count == 0)
            {
                return null;
            }
            
            heatMap.FlushAndUnload();

            return vectorTile;
        }
    }
}