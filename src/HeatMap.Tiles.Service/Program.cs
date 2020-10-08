using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace HeatMap.Tiles.Service
{
    class Program
    {
        internal const string EnvVarPrefix = "BIKEDATA_";

        static async Task Main(string[] args)
        {
            try
            {
                var configuration = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", true, true)
                    .AddJsonFile("/var/app/config/appsettings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables((c) => { c.Prefix = EnvVarPrefix; })
                    .Build();

                // setup serilog logging (from configuration).
                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(configuration)
                    .CreateLogger();
                try
                {
                    var connectionString = await File.ReadAllTextAsync(configuration[$"DB"]);
                    var minUsers = int.Parse(configuration[$"MIN_USERS"]);
                    var maxContributions = int.Parse(configuration[$"MAX_CONTRIBUTIONS"]);
                    var data = configuration["data"];
                    var output = configuration["output"];

                    // setup DI.
                    var serviceProvider = new ServiceCollection()
                        .AddLogging(b => { b.AddSerilog(); })
                        .AddSingleton<Worker>()
                        .AddSingleton(new WorkerConfiguration()
                        {
                            DataPath = data,
                            ConnectionString = connectionString,
                            OutputPath = output,
                            UserThreshold = minUsers,
                            MaxContributions = maxContributions
                        })
                        .BuildServiceProvider();

                    //do the actual work here
                    var task = serviceProvider.GetService<Worker>();
                    await task.RunAsync();
                }
                catch (Exception e)
                {
                    Log.Logger.Fatal(e, "Unhandled exception.");
                }
            }
            catch (Exception e)
            {
                // log to console if something happens before logging gets a chance to bootstrap.
                Console.WriteLine(e);
                throw;
            }
        }
    }
}
