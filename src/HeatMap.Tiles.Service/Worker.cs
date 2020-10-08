using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatMap.Tiles.Diffs;
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
        private const int MaxContributions = 10;
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
            var lockFile = Path.Combine(_configuration.DataPath, "heatmap-service.lock");
            // if (LockHelper.IsLocked(lockFile, TimeSpan.TicksPerDay))
            // {
            //     return;
            // }

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
                var newLatest = state.LastContributionId + MaxContributions;

                // select all data in this range.
                try
                {
                    Log.Verbose($"Updating tiles for [{state.LastContributionId+1},{newLatest}].");
                    
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
                    
                    // update user heatmaps.
                    var modifiedTiles = new HashSet<(uint x, uint y, int z)>();
                    foreach (var (userId, tracks) in perUser)
                    {
                        modifiedTiles.UnionWith(this.UpdateUserHeatmap(userId, tracks));
                    }
                    Log.Verbose($"Added {contributionsCount} in total to user heatmaps.");
                    
                    // update all modified tiles.
                    var heatMap = new HeatMap(heatMapPath, Resolution);
                    foreach (var modifiedTile in modifiedTiles)
                    {
                        // completely remove tile at lowest level.
                        heatMap.TryRemoveTile(modifiedTile);
                        
                        // load the heatmaps of all users in this tile.
                        foreach (var userId in this.GetUsersFor(modifiedTile))
                        {
                            using (var userHeatmap = this.GetOrCreateUserHeatMap(userId))
                            {
                                userHeatmap.CopyTilesTo(heatMap, new[] { modifiedTile });
                            }
                        }
                    }
                    
                    // build vector tiles.
                    // log when tiles are written.
                    var vectorTiles = heatMap.ToVectorTiles(modifiedTiles.Select(tile =>
                    {
                        //Log.Verbose($"Writing tile {tile.z}/{tile.x}/{tile.y}.mvt...");
                    
                        return tile;
                    }));

                    // write the tiles to disk as mvt.
                    Log.Verbose("Writing tiles...");
                    vectorTiles.Write(_configuration.OutputPath);
                    Log.Verbose("Tiles written!");

                    if (latestContributionId != null) state.LastContributionId = latestContributionId.Value;
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

        /// <summary>
        /// Update the single user heat map.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="tracks">The tracks.</param>
        /// <returns>The tiles modified.</returns>
        private IEnumerable<(uint x, uint y, int z)> UpdateUserHeatmap(string userId, IEnumerable<(Geometry geometry, long contributionId)> tracks)
        {
            // generate diff.
            var heatMapDiff = new HeatMapDiff(HeatMapZoom, Resolution);
            var lastContribution = -1L;
            foreach (var track in tracks)
            {
                // add to heat map and keep modified tiles.
                heatMapDiff.Add(track.geometry);

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

        private HeatMap GetOrCreateUserHeatMap(string userId)
        {
            var usersPath = Path.Combine(_configuration.DataPath, "users");
            if (!Directory.Exists(usersPath)) Directory.CreateDirectory(usersPath);
            var userHeatmapPath = Path.Combine(usersPath, userId);
            if (!Directory.Exists(userHeatmapPath)) Directory.CreateDirectory(userHeatmapPath);
            return new HeatMap(userHeatmapPath, Resolution);
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