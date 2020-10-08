using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatMap.Tiles.Diffs;
using Microsoft.Extensions.Logging;
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
        private const int MaxDays = 1;
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
                        vectorTiles.Write(_configuration.OutputPath);
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
    }
}