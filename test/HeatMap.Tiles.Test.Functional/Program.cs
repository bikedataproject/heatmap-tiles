using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatMap.Tiles.Diffs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using Npgsql;
using Serilog;

namespace HeatMap.Tiles.Test.Functional
{
    class Program
    {
        static async Task Main(string[] args)
        {            
            // read configuration.
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddUserSecrets<Program>()
                .Build();
            
            // setup serilog logging (from configuration).
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
            
            // get database connection.
            var connectionString = configuration["BikeDataProject:ConnectionString"];
            
            // setup DI.
            var serviceProvider = new ServiceCollection()
                .AddLogging(b =>
                {
                    b.AddSerilog();
                })
                .AddSingleton<FunctionalTestTask>()
                .AddSingleton(new FunctionalTestTaskConfiguration()
                {
                    ConnectionString = connectionString,
                    HeatMapPath = configuration["heatmap"],
                    VectorTilesPath = configuration["vector_tiles"]
                })
                .BuildServiceProvider();
            
            //do the actual work here
            var task = serviceProvider.GetService<FunctionalTestTask>();
            task.Run();
        }
    }
}
