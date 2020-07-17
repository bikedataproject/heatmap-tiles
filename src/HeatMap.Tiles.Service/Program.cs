using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatMap.Tiles.Diffs;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using Npgsql;
using Serilog;

namespace HeatMap.Tiles.Service
{
    class Program
    {
        private const string StateFileName = "state.json";
        private const uint Resolution = 512;
        private const int HeatMapZoom = 14;
        private const int MaxDays = 7;
        
        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();

            EnableLogging(config);

            var connectionString = config["connectionString"];
            var path = config["path"];
            var tiles = config["tiles"];

            await UpdateHeatMap(connectionString, path, tiles);
        }

        private static async Task UpdateHeatMap(string connectionString, string path, string tilesPath)
        {
            if (!Directory.Exists(path))
            {
                Log.Fatal($"Output path doesn't exist: {path}.");
                return;
            }
            if (!Directory.Exists(tilesPath))
            {
                Log.Fatal($"Output tiles path doesn't exist: {tilesPath}.");
                return;
            }
            
            var stateFile = Path.Combine(path, StateFileName);
            var heatMapPath = Path.Combine(path, "heatmap-cache");           
            var lockFile = Path.Combine(path, "heatmap-service.lock");
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

                await using var cn = new NpgsqlConnection(connectionString);
                cn.Open();
                cn.TypeMapper.UseNetTopologySuite();

                // get latest.
                var latest = await cn.GetLatestContributionTimeStamp();

                // check for more recent data.
                if (state != null && state.TimeStamp >= latest) return; // there is no more recent data.

                // process 1 day max.
                var earliest = state?.TimeStamp ?? await cn.GetEarliestContributionTimeStamp();
                if (earliest == null) return; // no data in database.
                var earliestAndMax = earliest.Value.AddDays(MaxDays);

                // select all data in this timespan.
                try
                {
                    Log.Verbose($"Updating tiles for [{earliest.Value},{earliestAndMax}[");
                    var heatMapDiff = new HeatMapDiff(HeatMapZoom, Resolution);
                    var heatMap = new HeatMap(heatMapPath, Resolution);
                    var modifiedTiles = new HashSet<(uint x, uint y, int z)>();
                    var contributionsCount = 0;
                    var contributionsInDiff = 0;
                    await foreach (var (createdAt, geometry) in cn.GetDataForTimeWindow(earliest.Value, earliestAndMax))
                    {
                        // add to heatmap and keep modified tiles.
                        heatMapDiff.Add(geometry);
                        contributionsCount++;
                        contributionsInDiff++;
                        if (contributionsCount % 100 == 0) Log.Verbose($"Added {contributionsCount}...");
                        
                        // apply diff earlier if there are too many contributions.
                        if (contributionsInDiff >= 500)
                        {
                            Log.Verbose($"Applying diff early...");
                            modifiedTiles.UnionWith(heatMap.ApplyDiff(heatMapDiff, toResolution: _ => Resolution));
                            heatMapDiff = new HeatMapDiff(HeatMapZoom, Resolution);
                            contributionsInDiff = 0;
                        }

                        // update state.
                        state ??= new State();
                        state.TimeStamp = createdAt;
                    }
                    Log.Verbose($"Added {contributionsCount} in total.");
                    
                    // apply diff if some are left unapplied.
                    if (contributionsInDiff > 0)
                    {
                        Log.Verbose($"Applying diff...");
                        modifiedTiles.UnionWith(heatMap.ApplyDiff(heatMapDiff, toResolution: _ => Resolution));
                    }

                    // update state, make sure it's at least earliest and max.
                    state ??= new State();
                    state.TimeStamp = earliestAndMax;

                    if (contributionsCount > 0)
                    {
                        // build vector tiles.
                        // log when tiles are written.
                        var vectorTiles = heatMap.ToVectorTiles(modifiedTiles.Select(tile =>
                        {
                            Log.Verbose($"Writing tile {tile.z}/{tile.x}/{tile.y}.mvt...");
                    
                            return tile;
                        }));

                        // write the tiles to disk as mvt.
                        Log.Verbose("Writing tiles...");
                        vectorTiles.Write(tilesPath);
                    }
                }
                catch (Exception e)
                {
                    Log.Fatal(e, "An unhandled fatal exception occurred while updating heatmap.");
                }
                finally
                {
                    if (state != null)
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

        private static void EnableLogging(IConfigurationRoot config)
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();
        }
    }
}
