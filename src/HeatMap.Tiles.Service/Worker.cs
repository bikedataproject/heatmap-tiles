using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatMap.Tiles.Diffs;
using HeatMap.Tiles.Draw;
using HeatMap.Tiles.IO.VectorTiles;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using Npgsql;
using Serilog;

namespace HeatMap.Tiles.Service
{
    public class Worker
    {        
        private const string StateFileName = "state.json";
        private const uint Resolution = 512;
        private const int HeatMapZoom = 14;
        private readonly ILogger<Worker> _logger;
        private readonly WorkerConfiguration _configuration;

        public Worker(WorkerConfiguration configuration, ILogger<Worker> logger)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public async Task RunAsync()
        {
            if (!Directory.Exists(_configuration.DataPath))
            {
                Log.Fatal($"Data path doesn't exist: {_configuration.DataPath}.");
                return;
            }
            if (!Directory.Exists(_configuration.OutputPath))
            {
                Log.Fatal($"Output tiles path doesn't exist: {_configuration.OutputPath}.");
                return;
            }
            
            var stateFile = Path.Combine(_configuration.DataPath, StateFileName);
            var heatMapPath = Path.Combine(_configuration.DataPath, "heatmap-cache");
            var heatMapMaskPath = Path.Combine(_configuration.DataPath, "heatmap-mask");
            var lockFile = Path.Combine(_configuration.DataPath, "heatmap-service.lock");
            if (LockHelper.IsLocked(lockFile, TimeSpan.TicksPerDay))
            {
                return;
            }

            try
            {
                LockHelper.WriteLock(lockFile);
                
                State state = null;
                if (File.Exists(stateFile))
                {
                    state = System.Text.Json.JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(stateFile));
                }

                await using var cn = new NpgsqlConnection(_configuration.ConnectionString);
                cn.Open();
                cn.TypeMapper.UseNetTopologySuite();

                // get latest.
                var latest = await cn.GetLatestContributionId();

                // check for more recent data.
                if (state != null && state.LastContributionId >= latest) return; // there is no more recent data.

                // process n contributions max.
                state ??= new State()
                {
                    LastContributionId = -1
                };

                // select all data in this range.
                try
                {
                    // get contributions.
                    var (perUser, contributionsCount, latestContributionId) = await this.GetContributionsPerUser(cn, state);
                    
                    // update user heatmaps.
                    var modifiedTiles = new HashSet<(uint x, uint y, int z)>();
                    foreach (var (userId, tracks) in perUser)
                    {
                        modifiedTiles.UnionWith(this.UpdateUserHeatmap(userId, tracks));
                    }
                    Log.Debug($"Added {contributionsCount} in total to user heatmaps.");

                    // update the heat map mask.
                    var heatMapMask = this.UpdateMask(heatMapMaskPath, modifiedTiles);
                    
                    // update the heat map.
                    var (heatMap, updatedTiles) = this.UpdateHeatMap(heatMapPath, modifiedTiles, heatMapMask);
                    
                    // rebuilding parent tile tree.
                    Log.Debug($"Updating parent tile tree for {updatedTiles.Count} update tiles...");
                    var parentTiles = heatMap.RebuildParentTileTree(updatedTiles);
                    updatedTiles.UnionWith(parentTiles);
                    
                    // write vector tiles.
                    this.WriteVectorTiles(heatMap, updatedTiles);
                    
                    // write vector tiles mask.
                    this.WriteVectorTilesMask(heatMapMask, updatedTiles);

                    if (latestContributionId != null) state.LastContributionId = latestContributionId.Value;
                    Log.Debug("Done!");
                }
                catch (Exception e)
                {
                    Log.Fatal(e, "An unhandled fatal exception occurred while updating heatmap.");
                }
                finally
                {
                    await File.WriteAllTextAsync(stateFile, System.Text.Json.JsonSerializer.Serialize(state));
                }
            }
            catch (Exception e)
            {
                Log.Fatal(e, "An unhandled fatal exception occurred while updating heatmap.");
            }
            finally
            {
                File.Delete(lockFile);
            }
        }

        private async Task<(Dictionary<string, List<(Geometry geometry, long contributionId)>> perUser, int count, long? latestContributionId)> GetContributionsPerUser(NpgsqlConnection cn, State state)
        {
            var newLatest = state.LastContributionId + _configuration.MaxContributions;
            Log.Debug($"Getting contributions for [{state.LastContributionId+1},{newLatest}].");
            
            // collect all tracks per user.
            var perUser = new Dictionary<string, List<(Geometry geometry, long contributionId)>>();
            long? latestContributionId = null;
            var contributionsCount = 0;
            await foreach (var (contributionId, geometry, userId) in cn.GetDataForWindow(state.LastContributionId, newLatest))
            {
                if (!perUser.TryGetValue(userId, out var geometries))
                {
                    geometries = new List<(Geometry geometry, long contributionId)>();
                    perUser[userId] = geometries;
                }
                geometries.Add((geometry, contributionId));

                // keep state of last track.
                latestContributionId = contributionId;
                contributionsCount++;
            }

            return (perUser, contributionsCount, latestContributionId);
        }

        private void WriteVectorTiles(HeatMap<uint> heatMap, HashSet<(uint x, uint y, int z)> updatedTiles)
        {
            // build vector tiles.
            // log when tiles are written.
            var vectorTiles = heatMap.ToVectorTiles(updatedTiles);

            // write the tiles to disk as mvt.
            Log.Debug($"Writing {updatedTiles.Count} tiles...");
            vectorTiles.Select(x =>
            {
                var tile = new NetTopologySuite.IO.VectorTiles.Tiles.Tile(x.TileId);
                Log.Verbose($"Writing {tile.X}/{tile.Y}/{tile.Zoom} tiles...");
                
                return x;
            }).Write(_configuration.OutputPath);
        }

        private void WriteVectorTilesMask(HeatMap<uint> heatMap, HashSet<(uint x, uint y, int z)> updatedTiles)
        {
            // build mask vector tiles.
            // log when tiles are written.
            var vectorTiles = heatMap.ToVectorTiles(updatedTiles);

            // write the tiles to disk as mvt.
            Log.Debug($"Writing {updatedTiles.Count} mask tiles...");
            var maskPath = Path.Combine(_configuration.OutputPath, "mask");
            if (Directory.Exists(maskPath)) Directory.CreateDirectory(maskPath);
            vectorTiles.Write(maskPath);
        }

        private (HeatMap<uint> heatMap, HashSet<(uint x, uint y, int z)> updateTiles) UpdateHeatMap(string heatMapPath, IEnumerable<(uint x, uint y, int z)> modifiedTiles,
            HeatMap<uint> heatMapMask)
        {
            // update all modified tiles.
            var heatMap = new HeatMap<uint>(heatMapPath, Resolution);
            var updatedTiles = new HashSet<(uint x, uint y, int z)>();
            foreach (var modifiedTile in modifiedTiles)
            {
                // completely remove tile at lowest level.
                heatMap.TryRemoveTile(modifiedTile);
                        
                // get mask tile.
                updatedTiles.Add(modifiedTile);
                if (!heatMapMask.TryGetTile(modifiedTile, out var maskTile)) continue;
                Log.Verbose($"Tile updated with user {_configuration.UserThreshold} threshold: {modifiedTile}");
                        
                // load the heatmaps of all users in this tile.
                foreach (var userId in this.GetUsersFor(modifiedTile))
                {
                    using (var userHeatmap = this.GetOrCreateUserHeatMap(userId))
                    {
                        userHeatmap.AddTilesTo(heatMap, new[] { modifiedTile }, (_, l, value) =>
                        {
                            var mask = maskTile[l.x, l.y];
                            if (mask == 0) return 0;
                            if (mask < _configuration.UserThreshold)
                            {
                                //Log.Debug($"Mask value found: {mask}");
                                return 0;
                            }

                            return value;
                        });
                    }
                }
                
                // make sure we do only one tile at a time.        
                heatMapMask.FlushAndUnload();
                heatMap.FlushAndUnload(); 
            }

            return (heatMap, updatedTiles);
        }

        private HeatMap<uint> UpdateMask(string heatMapMaskPath, IEnumerable<(uint x, uint y, int z)> modifiedTiles)
        {
            // update masks for all modified tiles.
            var heatMapMask = new HeatMap<uint>(heatMapMaskPath, Resolution);
            foreach (var modifiedTile in modifiedTiles)
            {
                heatMapMask.TryRemoveTile(modifiedTile);
                        
                var users = this.GetUsersFor(modifiedTile).ToList();
                if (users.Count < _configuration.UserThreshold)
                {
                    Log.Verbose($"Tile modified below {_configuration.UserThreshold} user threshold: {modifiedTile} with {users.Count}");
                    continue;
                }
                        
                Log.Verbose($"Tile modified with {_configuration.UserThreshold} user threshold: {modifiedTile} with {users.Count}");
                foreach (var userId in users)
                {
                    using (var userHeatmap = this.GetOrCreateUserHeatMap(userId))
                    {
                        userHeatmap.AddTilesTo(heatMapMask, new [] {modifiedTile},
                            ((tile, location, targetValue, sourceValue) =>
                            {
                                // there is no data here!
                                if (sourceValue == 0) return targetValue;
                                        
                                return (byte)(targetValue + 1);
                            }));
                    }
                }
            }
            
            heatMapMask.FlushAndUnload();

            return heatMapMask;
        }

        /// <summary>
        /// Update the single user heat map.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="tracks">The tracks.</param>
        /// <returns>The tiles modified.</returns>
        private IEnumerable<(uint x, uint y, int z)> UpdateUserHeatmap(string userId, IEnumerable<(Geometry geometry, long contributionId)> tracks)
        {
            Log.Verbose($"Updating user {userId} heatmap with {tracks.Count()} contributions.");
            
            // generate diff.
            var heatMapDiff = new HeatMapDiff(HeatMapZoom, Resolution);
            var lastContribution = -1L;
            foreach (var track in tracks)
            {
                // add to heat map and keep modified tiles.
                heatMapDiff.Draw(track.geometry);

                if (lastContribution < track.contributionId) lastContribution = track.contributionId;
            }
            if (lastContribution < 0) return Enumerable.Empty<(uint x, uint y, int z)>();

            // update/create user heatmap.
            using var userHeatmap = this.GetOrCreateUserHeatMap(userId);
            var tiles = userHeatmap.ApplyDiff(heatMapDiff, minZoom: 14).ToList();
            this.SetLastContributionIdForUser(userId, lastContribution);
            
            // update tiles->users map.
            this.AddUserTo(userId, tiles);
            
            return tiles;
        }

        private HeatMap<uint> GetOrCreateUserHeatMap(string userId)
        {
            var usersPath = Path.Combine(_configuration.DataPath, "users");
            if (!Directory.Exists(usersPath)) Directory.CreateDirectory(usersPath);
            var userHeatmapPath = Path.Combine(usersPath, userId);
            if (!Directory.Exists(userHeatmapPath)) Directory.CreateDirectory(userHeatmapPath);
            return new HeatMap<uint>(userHeatmapPath, Resolution);
        }

        private void SetLastContributionIdForUser(string userId, long contributionId)
        {
            var usersPath = Path.Combine(_configuration.DataPath, "users");
            if (!Directory.Exists(usersPath)) Directory.CreateDirectory(usersPath);
            var userHeatmapPath = Path.Combine(usersPath, userId);
            File.WriteAllText(Path.Combine(userHeatmapPath, "state.txt"), contributionId.ToString());
        }

        private void AddUserTo(string userId, IEnumerable<(uint x, uint y, int z)> tiles)
        {
            foreach (var tile in tiles)
            {
                var tilesPath = Path.Combine(_configuration.DataPath, "tiles");
                if (!Directory.Exists(tilesPath)) Directory.CreateDirectory(tilesPath);
                var zoomPath = Path.Combine(tilesPath, $"{tile.z}");
                if (!Directory.Exists(zoomPath)) Directory.CreateDirectory(zoomPath);
                var xPath = Path.Combine(zoomPath, $"{tile.x}");
                if (!Directory.Exists(xPath)) Directory.CreateDirectory(xPath);
                var file = Path.Combine(xPath, $"{tile.y}.users");
                var users = new List<string> {userId};
                if (File.Exists(file))
                {
                    foreach (var existingUserId in File.ReadLines(file))
                    {
                        if (existingUserId == userId)
                        {
                            users.Clear();
                            break;
                        }
                        users.Add(existingUserId);
                    }
                }
                if (users.Count > 0) File.WriteAllLines(file, users);
            }
        }

        private IEnumerable<string> GetUsersFor((uint x, uint y, int z) tile)
        {
            var tilesPath = Path.Combine(_configuration.DataPath, "tiles");
            if (!Directory.Exists(tilesPath)) return Enumerable.Empty<string>();
            var zoomPath = Path.Combine(tilesPath, $"{tile.z}");
            if (!Directory.Exists(zoomPath)) return Enumerable.Empty<string>();
            var xPath = Path.Combine(zoomPath, $"{tile.x}");
            if (!Directory.Exists(xPath)) return Enumerable.Empty<string>();
            var file = Path.Combine(xPath, $"{tile.y}.users");
            if (!File.Exists(file)) return Enumerable.Empty<string>();

            return File.ReadAllLines(file);
        }
    }
}