using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeatMap.Tiles.Diffs;
using HeatMap.Tiles.Draw;
using HeatMap.Tiles.IO.VectorTiles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using Npgsql;
using Serilog;

namespace HeatMap.Tiles.Service
{
    public class Worker : BackgroundService
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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
            
            _logger.LogInformation("Worker running at: {time}, triggered every {refreshTime}", 
                DateTimeOffset.Now, _configuration.RefreshTime);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                var processedContributions = await this.RunAsync();
                
                // if refresh time is 0 don't refresh.
                if (_configuration.RefreshTime == 0) return;
                
                // check every refresh time for new contributions 
                // but move on immediately if new contributions were processed last run.
                if (processedContributions == 0) await Task.Delay(_configuration.RefreshTime, stoppingToken);
            }
        }

        private async Task<int> RunAsync()
        {
            var stateFile = Path.Combine(_configuration.DataPath, StateFileName);
            var heatMapPath = Path.Combine(_configuration.DataPath, "heatmap");
            
            try
            {
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
                if (state != null && state.LastContributionId >= latest) return 0; // there is no more recent data.

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
                    Log.Information($"Added {contributionsCount} until {latestContributionId} in total to {perUser.Count} user heatmaps.");

                    // update the heat map and return the updates tiles.
                    var (heatMap, updatedTiles) = this.UpdateHeatMap(heatMapPath, modifiedTiles);
                    
                    // update the parent heat map.
                    Log.Debug($"Updating parent tile tree for {updatedTiles.Count} updated tiles...");
                    var userCountParentTiles = heatMap.RebuildParentTileTree(updatedTiles);
                    updatedTiles.UnionWith(userCountParentTiles);
                    
                    // write user count tiles.
                    this.WriteVectorTiles(heatMap, updatedTiles);

                    if (latestContributionId != null) state.LastContributionId = latestContributionId.Value;
                    Log.Debug("Done!");

                    return contributionsCount;
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

            return 0;
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
                    if (perUser.Count >= _configuration.MaxUsers) break; 
                    geometries = new List<(Geometry geometry, long contributionId)>();
                    perUser[userId] = geometries;
                }
                geometries.Add((geometry, contributionId));

                // keep state of last track.
                latestContributionId = contributionId;
                contributionsCount++;
            }

            Log.Debug($"Got {contributionsCount} contributions for {perUser.Count} users.");
            return (perUser, contributionsCount, latestContributionId);
        }

        private (HeatMap<ulong> heatMap, HashSet<(uint x, uint y, int z)> updateTiles) UpdateHeatMap(string heatMapPath, IEnumerable<(uint x, uint y, int z)> modifiedTiles)
        {
            // update user tiles for all modified tiles.
            var heatMap = new HeatMap<ulong>(heatMapPath, Resolution);
            var updatedTiles = new HashSet<(uint x, uint y, int z)>();
            foreach (var modifiedTile in modifiedTiles)
            {
                // remove existing user tile.
                if (heatMap.TryRemoveTile(modifiedTile)) updatedTiles.Add(modifiedTile);
                        
                // get user tiles.
                var users = this.GetUsersFor(modifiedTile).ToList();
                if (users.Count < _configuration.UserThreshold) continue;
                
                // add all user data.
                updatedTiles.Add(modifiedTile);
                Log.Verbose($"Tile modified with {_configuration.UserThreshold} user threshold: {modifiedTile} with {users.Count}");
                foreach (var userId in users)
                {
                    using (var userHeatmap = this.GetOrCreateUserHeatMap(userId))
                    {
                        userHeatmap.AddTilesTo(heatMap, new [] {modifiedTile},
                            (tile, location, existingTargetValue, userValue) =>
                            {
                                // there is no data here!
                                if (userValue == 0) return existingTargetValue;
                                
                                // decode existing value.
                                var (userCount, tripCount) = Encoder.Decode(existingTargetValue);
                                
                                // add user data and encode.
                                return Encoder.Encode(userCount + 1, tripCount + userValue);
                            });
                    }
                }
                
                // apply threshold.
                if (heatMap.TryGetTile(modifiedTile, out var newTile))
                {
                    newTile.UpdateValues(v =>
                    {
                        // decode existing value.
                        var (userCount, _) = Encoder.Decode(v.value);
                        
                        // if the user count is lower than threshold just remove the sample.
                        if (userCount < (ulong)_configuration.UserThreshold) return 0;
                
                        return v.value;
                    });
                }
            }
            
            heatMap.FlushAndUnload();

            return (heatMap, updatedTiles);
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
        
        private void WriteVectorTiles(HeatMap<ulong> heatMap, HashSet<(uint x, uint y, int z)> updatedTiles)
        {
            // build vector tiles.
            // log when tiles are written.
            var vectorTiles = heatMap.ToVectorTiles(updatedTiles, (t, v) =>
            {
                var (userCount, tripCount) = Encoder.Decode(v);

                if (userCount == 0) return null;
                
                return new AttributesTable {{"users", userCount}, {"trips", tripCount}};
            });

            // write the tiles to disk as mvt.
            Log.Debug($"Writing {updatedTiles.Count} tiles...");
            vectorTiles.Select(x =>
            {
                var tile = new NetTopologySuite.IO.VectorTiles.Tiles.Tile(x.TileId);
                Log.Verbose($"Writing {tile.X}/{tile.Y}/{tile.Zoom} tiles...");
                
                return x;
            }).Write(_configuration.OutputPath);
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