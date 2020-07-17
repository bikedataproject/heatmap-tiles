using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using Npgsql;

namespace HeatMap.Tiles.Test.Functional
{
    class Program
    {
        static async Task Main(string[] args)
        {
            //
            var features = (new GeoJsonReader()).Read<FeatureCollection>(
            //     //File.ReadAllText(@"/data/ANYWAYS Dropbox/data/bike-data-project/test-belgium.geojson"));
                 File.ReadAllText(@"/data/ANYWAYS Dropbox/data/bike-data-project/temp-small.geojson"));
            var geometries = features.Select(x => x.Geometry);
          
            HeatMap heatMap;
            var resolution = 512U;
            var before = DateTime.Now.Ticks;
            Console.WriteLine("Adding features...");
            using (var stream = File.Open("/media/xivk/2T-SSD-EXT/temp/heatmap.map", FileMode.Create))
            {
                //var stream = new MemoryStream();
                
                heatMap = new HeatMap(stream,
                    14, resolution);
                var f = 0;
                foreach (var geometry in geometries)
                {
                    heatMap.Add(geometry, 1);
                    Console.WriteLine($"Feature {f + 1}...");
                    f++;
                }
            }

            // open existing.
            var readStream = File.Open("heatmap.map", FileMode.Open);
            heatMap = new HeatMap(readStream);
            
            // convert to a vector tile tree.d
            var tree = heatMap.ToVectorTiles(12, 14, i =>
            {
                if (i == 14) return resolution;
            
                return resolution;
            });

            tree = tree.Select(x =>
            {
                var tile = new NetTopologySuite.IO.VectorTiles.Tiles.Tile(x.TileId);
                Console.WriteLine($"Writing tile {tile}...");

                return x;
            });
            
            // write the tiles to disk as mvt.
            Console.WriteLine("Writing tiles...");
            tree.Write(@"/data/work/anyways/projects/werkvennootschap/heatmap-experiment/tiles");
            
            
            var after = DateTime.Now.Ticks;
            Console.WriteLine($"Processing fininshed in {new TimeSpan(after - before).TotalSeconds}");
        }
    }
}
