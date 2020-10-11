using System.Collections.Generic;
using System.Linq;
using HeatMap.Tiles.Diffs;
using HeatMap.Tiles.Draw;
using HeatMap.Tiles.IO.VectorTiles;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using Npgsql;

namespace HeatMap.Tiles.Test.Functional
{
    public class FunctionalTestTask
    {
        private readonly ILogger<FunctionalTestTask> _logger;
        private readonly FunctionalTestTaskConfiguration _configuration;
        private readonly NpgsqlConnection _connection;
        private readonly int _zoom = 8;
        private readonly uint _resolution = 256;
        
        public FunctionalTestTask(FunctionalTestTaskConfiguration configuration, ILogger<FunctionalTestTask> logger)
        {
            _configuration = configuration;
            _logger = logger;
            
            _connection = new NpgsqlConnection(configuration.ConnectionString + ";Command Timeout=0");
        }

        public void Run()
        {
            // open db connection.
            _connection.Open();
            _connection.TypeMapper.UseNetTopologySuite();
            
            var heatMap = new HeatMap<uint>(_configuration.HeatMapPath, _resolution);
            var heatMapDiff = new HeatMapDiff(14, _resolution);
            
            // get all tiles with data.
            var tilesWithData = this.GetNonEmptyTiles(_zoom);
            
            // loop over all tiles at a given zoom and build diff per tile.
            var modifiedTiles = new HashSet<(uint x, uint y, int z)>();
            //for (var i = 0; i < tilesWithData.Count; i++)
            var t = 0;
            var minDiffTiles = 10;
            foreach (var (x, y) in tilesWithData)
            {
                t++;
                //var (x, y) = tilesWithData[i];
                
                // build diff for the given tile.
                this.AddToDiff(heatMapDiff, x, y, _zoom);
                
                // check tile count.
                var diffTiles = heatMapDiff.Count();
                _logger.LogInformation($"Built for tile #{t+1}: {_zoom}/{x}/{y} {diffTiles} tiles in diff...");
                if (diffTiles < minDiffTiles) continue;

                // apply diff and keep modified tiles.
                modifiedTiles.UnionWith(heatMap.ApplyDiff(heatMapDiff, 0));
                heatMapDiff.Clear();
                _logger.LogInformation($"Diff applied...");
                
                // unload the used tiles.
                heatMap.FlushAndUnload();
            }
            
            // apply the last tiles if any.
            modifiedTiles.UnionWith(heatMap.ApplyDiff(heatMapDiff, 0));
            
            // build & write vector tiles.
            var vectorTiles = heatMap.ToVectorTiles(modifiedTiles);
            vectorTiles = vectorTiles.Select(x =>
            {
                var tile = new NetTopologySuite.IO.VectorTiles.Tiles.Tile(x.TileId);
                _logger.LogInformation($"Writing vector tile {tile}...");
                return x;
            });
            
            // write the tiles to disk as mvt.
            vectorTiles.Write(_configuration.VectorTilesPath);
        }

        private IEnumerable<(uint x, uint y)> GetNonEmptyTiles(int zoom)
        {
            var minZoom = 1;
            if (zoom == minZoom)
            {
                var tilesOrdinals = 1 << minZoom;
                for (uint x = 0; x < tilesOrdinals; x++)
                for (uint y = 0; y < tilesOrdinals; y++)
                {
                    if (!HasData(x, y, minZoom)) continue;

                    yield return (x, y);
                }
            }
            else
            {
                var tilesOrdinals = 1 << minZoom;
                for (uint x = 0; x < tilesOrdinals; x++)
                for (uint y = 0; y < tilesOrdinals; y++)
                {
                    if (!HasData(x, y, minZoom)) continue;

                    foreach (var subTile in GetNonEmptySubTiles((x, y, minZoom), zoom))
                    {
                        yield return subTile;
                    }
                }
            }
        }

        private IEnumerable<(uint x, uint y)> GetNonEmptySubTiles((uint x, uint y, int z) tile, int zoom)
        {
            if (tile.z == zoom)
            {
                if (HasData(tile.x, tile.y, tile.z)) yield return (tile.x, tile.y);
                yield break;
                
            }

            foreach (var subTile in tile.SubTilesFor(tile.z + 1))
            {
                if (!HasData(subTile.x, subTile.y, tile.z + 1)) continue;
                
                foreach (var tileWithData in GetNonEmptySubTiles((subTile.x, subTile.y, tile.z + 1), zoom))
                {
                    yield return tileWithData;
                }
            }
        }

        private bool HasData(uint x, uint y, int z)
        {
            // query the database for all tracks in this tile.
            // https://gis.stackexchange.com/questions/25797/select-bounding-box-using-postgis
            var box = TileStatic.Box(z, TileStatic.ToLocalId(x, y, z));
            
            var sql = $"select * from contributions where contributions.points_geom && " +
                           $"ST_MakeEnvelope({box.topLeft.longitude}, {box.bottomRight.latitude}, {box.bottomRight.longitude}, {box.topLeft.latitude}, 4326) limit 1";
            
            var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            return reader.Read();
        }

        private IEnumerable<Geometry> GetGeometriesForTile(uint x, uint y, int z)
        {            
            // query the database for all tracks in this tile.
            // https://gis.stackexchange.com/questions/25797/select-bounding-box-using-postgis
            var box = TileStatic.Box(z, TileStatic.ToLocalId(x, y, z));

            var sql = $"select * from contributions where contributions.points_geom && " +
                      $"ST_MakeEnvelope({box.topLeft.longitude}, {box.bottomRight.latitude}, {box.bottomRight.longitude}, {box.topLeft.latitude}, 4326)";
            var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();

            var pointsGeomOrdinal = reader.GetOrdinal("points_geom");
            var g = 0;
            while (reader.Read())
            {
                var data = reader[pointsGeomOrdinal];
                if (!(data is Geometry geometry)) continue;
                
                g++;
                yield return geometry;
            }
            _logger.LogInformation($"{g} geometries found!");
        }

        private void AddToDiff(HeatMapDiff heatMapDiff, uint x, uint y, int z)
        {
            // add all geometries in the tile.
            heatMapDiff.Draw(this.GetGeometriesForTile(x, y, z), includeTile: t =>
            {
                var tile = TileStatic.ToTile(heatMapDiff.Zoom, t);
                var parent = (tile.x, tile.y, heatMapDiff.Zoom).ParentTileFor(z);
                if (parent.x != x) return false;
                if (parent.y != y) return false;
                return true;
            });
        }
    }
}