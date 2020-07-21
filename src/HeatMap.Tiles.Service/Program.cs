using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
        private const int MaxDays = 1;
        
        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();

            EnableLogging(config);

            var connectionString = config["connectionString"];
            var path = config["path"];
            var tiles = config["tiles"];
            
            var startTicks = DateTime.Now.Ticks;
            while (true)
            {
                // try update and make sure to minimum 1 sec passed.
                var startUpdate = DateTime.Now.Ticks;   
                await UpdateHeatMap(connectionString, path, tiles);
                var updateSpan = new TimeSpan(DateTime.Now.Ticks - startUpdate);
                var seconds = 1000 - updateSpan.TotalMilliseconds;
                if (seconds > 0)
                {
                    Thread.Sleep(1000);
                }
                
                // if almost a minute passed, stop.
                var time = new TimeSpan(DateTime.Now.Ticks - startTicks);
                if (time.TotalSeconds > 45)
                {
                    break;
                }
            }
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
                    var contributionsCount = 0;
                    var tooManyContributions = false;
                    await foreach (var (createdAt, geometry) in cn.GetDataForTimeWindow(earliest.Value, earliestAndMax))
                    {
                        // apply diff earlier if there are too many contributions.
                        if (contributionsCount >= 200 && state != null &&
                            state.TimeStamp != createdAt)
                        {
                            Log.Verbose($"Stopped at {state.TimeStamp}, too many contributions...");
                            tooManyContributions = true;
                            break;
                        }
                        
                        // add to heat map and keep modified tiles.
                        heatMapDiff.Add(geometry);
                        contributionsCount++;
                        if (contributionsCount % 100 == 0) Log.Verbose($"Added {contributionsCount}...");

                        // update state.
                        state ??= new State();
                        state.TimeStamp = createdAt;
                    }
                    Log.Verbose($"Added {contributionsCount} in total.");

                    // update state, make sure it's at least earliest and max.
                    // in case of no contributions make sure to move on.
                    if (!tooManyContributions) state = new State {TimeStamp = earliestAndMax};

                    if (contributionsCount > 0)
                    {
                        using var heatMap = new HeatMap(heatMapPath, Resolution);
                        
                        Log.Verbose($"Applying diff...");
                        var modifiedTiles = new HashSet<(uint x, uint y, int z)>();
                        modifiedTiles.UnionWith(heatMap.ApplyDiff(heatMapDiff, toResolution: _ => Resolution));
                        
                        // build vector tiles.
                        // log when tiles are written.
                        var vectorTiles = heatMap.ToVectorTiles(modifiedTiles.Select(tile =>
                        {
                            //Log.Verbose($"Writing tile {tile.z}/{tile.x}/{tile.y}.mvt...");
                    
                            return tile;
                        }));

                        // write the tiles to disk as mvt.
                        Log.Verbose("Writing tiles...");
                        vectorTiles.Write(tilesPath);
                        Log.Verbose("Tiles written!");
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
