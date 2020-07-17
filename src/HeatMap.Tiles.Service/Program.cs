using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using Npgsql;
using Serilog;

namespace HeatMap.Tiles.Service
{
    class Program
    {
        private const string StateFileName = "state.json";
        private const string HeatMapFileName = "heat.map";
        private const uint Resolution = 512;
        private const int HeatMapZoom = 14;
        private const int MaxDays = 30;
        private const int MinZoom = 8;
        private const int MaxZoom = 14;
        
        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();

            EnableLogging(config);

            var connectionString = config["connectionString"];
            var path = config["path"];

            await UpdateHeatMap(connectionString, path);
        }

        private static async Task UpdateHeatMap(string connectionString, string path)
        {
            if (!Directory.Exists(path))
            {
                Log.Fatal($"Output path doesn't exist: {path}.");
                return;
            }
            
            var stateFile = Path.Combine(path, StateFileName);
            var heatMapFile = Path.Combine(path, HeatMapFileName);           
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
                var modifiedTiles = new HashSet<(uint x, uint y)>();
                try
                {
                    Log.Verbose($"Updating tiles for [{earliest.Value},{earliestAndMax}[");
                    HeatMap heatMap = null;
                    var contributionsCount = 0;
                    await foreach (var (createdAt, geometry) in cn.GetDataForTimeWindow(earliest.Value, earliestAndMax))
                    {
                        // add to heatmap and keep modified tiles.
                        heatMap ??= OpenOrCreateHeatMap(heatMapFile);
                        modifiedTiles.UnionWith(heatMap.Add(geometry));
                        contributionsCount++;
                        if (contributionsCount % 100 == 0) Log.Verbose($"Added {contributionsCount}...");

                        // update state.
                        state ??= new State();
                        state.TimeStamp = createdAt;
                    }
                    Log.Verbose($"Added {contributionsCount} in total.");
                    
                    // update state, make sure it's at least earliest and max.
                    state ??= new State();
                    state.TimeStamp = earliestAndMax;

                    if (heatMap != null)
                    {
                        // update all modified tiles.
                        var tree = heatMap.ToVectorTiles(MinZoom, MaxZoom, i =>
                            {
                                if (i == 14) return Resolution;
                                return Resolution / 2;
                            },
                            modifiedTiles.ToTileFilter(heatMap.Zoom));
                        tree = tree.Select(x =>
                        {
                            var tile = new NetTopologySuite.IO.VectorTiles.Tiles.Tile(x.TileId);
                            Log.Verbose($"Writing tile {tile}...");
                    
                            return x;
                        });
                    
                        // write the tiles to disk as mvt.
                        Log.Verbose("Writing tiles...");
                        tree.Write(path);
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

        private static HeatMap OpenOrCreateHeatMap(string heatMapFile)
        {
            if (File.Exists(heatMapFile))
            {
                return new HeatMap(File.Open(heatMapFile, FileMode.Open));
            }
            
            return new HeatMap(File.Open(heatMapFile, FileMode.Create), HeatMapZoom, Resolution);
        }

        private static void EnableLogging(IConfigurationRoot config)
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();
        }
    }
}
