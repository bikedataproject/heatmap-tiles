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
                 File.ReadAllText(@"/data/ANYWAYS Dropbox/data/bike-data-project/test-brussels.geojson"));
                 //File.ReadAllText(@"/data/ANYWAYS Dropbox/data/bike-data-project/temp-small.geojson"));
            var geometries = features.Select(x => x.Geometry);
            //geometries = geometries.Skip(300).Take(100);
            
            // create the diff.
            Console.WriteLine("Creating the diff...");
            var heatMapDiff = new HeatMapDiff(14, 1024);
            var g = 0;
            foreach (var geometry in geometries)
            {
                heatMapDiff.Add(geometry);
                g++;
                Console.WriteLine($"Added {g} geometries...");
            }
            
            // apply the diff to the heatmap.
            Console.WriteLine("Applying the diff...");
            var heatMap = new HeatMap("heatmap");
            var tiles = heatMap.ApplyDiff(heatMapDiff, 0, i => 1024);
            
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
        }
    }
}
