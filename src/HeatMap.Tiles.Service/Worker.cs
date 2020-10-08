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
                    var perUser = new Dictionary<string, List<Geometry>>();
                    long? latestContributionId = null;
                    var contributionsCount = 0;
                    await foreach (var (contributionId, geometry, userId) in cn.GetDataForWindow(state.LastContributionId, newLatest))
                    {
                        if (!perUser.TryGetValue(userId, out var geometries))
                        {
                            geometries = new List<Geometry>();
                            perUser[userId] = geometries;
                        }
                        geometries.Add(geometry);

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
        private IEnumerable<(uint x, uint y, int z)> UpdateUserHeatmap(string userId, IEnumerable<Geometry> tracks)
        {
            // generate diff.
            var heatMapDiff = new HeatMapDiff(HeatMapZoom, Resolution);
            foreach (var track in tracks)
            {
                // add to heat map and keep modified tiles.
                heatMapDiff.Add(track);
            }

            // update/create user heatmap.
            var userHeatmapPath = Path.Combine(_configuration.DataPath, userId);
            if (!Directory.Exists(userHeatmapPath)) Directory.CreateDirectory(userHeatmapPath);
            using var userHeatmap = new HeatMap(userHeatmapPath, Resolution);
            var tiles = userHeatmap.ApplyDiff(heatMapDiff, minZoom: 14).ToList();
            
            // update tiles->users map.
            foreach (var tile in tiles)
            {
                var file = Path.Combine(_configuration.DataPath, $"{tile.z}-{tile.x}-{tile.z}.users");
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
            
            return tiles;
        }
    }
}