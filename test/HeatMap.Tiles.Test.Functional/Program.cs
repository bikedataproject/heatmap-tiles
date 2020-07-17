using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatMap.Tiles.Diffs;
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
            // load features.
            var features = (new GeoJsonReader()).Read<FeatureCollection>(
                 File.ReadAllText(@"/data/ANYWAYS Dropbox/data/bike-data-project/test.geojson"));
                 //File.ReadAllText(@"/data/ANYWAYS Dropbox/data/bike-data-project/temp-small.geojson"));
            var geometries = features.Select(x => x.Geometry);
            
            // create the diff.
            Console.WriteLine("Creating the diff...");
            var heatMapDiff = new HeatMapDiff(14, 1024);
            foreach (var geometry in geometries)
            {
                heatMapDiff.Add(geometry);
            }
            
            // apply the diff to the heatmap.
            Console.WriteLine("Applying the diff...");
            var heatMap = new HeatMap("heatmap");
            var tiles = heatMap.ApplyDiff(heatMapDiff, 0, i => 1024).ToList();
            
            // build & write vector tiles.
            var vectorTiles = heatMap.ToVectorTiles(tiles);
            vectorTiles = vectorTiles.Select(x =>
            {
                var tile = new NetTopologySuite.IO.VectorTiles.Tiles.Tile(x.TileId);
                Console.WriteLine($"Writing tile {tile}...");
            
                return x;
            });
            
            // write the tiles to disk as mvt.
            Console.WriteLine("Writing tiles...");
            vectorTiles.Write(@"/data/work/anyways/projects/werkvennootschap/heatmap-experiment/tiles");
          
            // HeatMapDiff heatMapDiff;
            // var resolution = 512U;
            // var before = DateTime.Now.Ticks;
            // Console.WriteLine("Adding features...");
            // using (var stream = File.Open("/media/xivk/2T-SSD-EXT/temp/heatmap.map", FileMode.Create))
            // {
            //     //var stream = new MemoryStream();
            //     
            //     heatMapDiff = new HeatMapDiff(stream,
            //         14, resolution);
            //     var f = 0;
            //     foreach (var geometry in geometries)
            //     {
            //         heatMapDiff.Add(geometry, 1);
            //         Console.WriteLine($"Feature {f + 1}...");
            //         f++;
            //     }
            // }
            //
            // // open existing.
            // var readStream = File.Open("heatmap.map", FileMode.Open);
            // heatMapDiff = new HeatMapDiff(readStream);
            //
            // // convert to a vector tile tree.d
            // var tree = heatMapDiff.ToVectorTiles(12, 14, i =>
            // {
            //     if (i == 14) return resolution;
            //
            //     return resolution;
            // });
            //
            // tree = tree.Select(x =>
            // {
            //     var tile = new NetTopologySuite.IO.VectorTiles.Tiles.Tile(x.TileId);
            //     Console.WriteLine($"Writing tile {tile}...");
            //
            //     return x;
            // });
            //
            // // write the tiles to disk as mvt.
            // Console.WriteLine("Writing tiles...");
            // tree.Write(@"/data/work/anyways/projects/werkvennootschap/heatmap-experiment/tiles");
            //
            //
            // var after = DateTime.Now.Ticks;
            // Console.WriteLine($"Processing fininshed in {new TimeSpan(after - before).TotalSeconds}");
        }
    }
}
