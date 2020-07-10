using System;
using System.IO;
using NetTopologySuite.Features;
using NetTopologySuite.IO;
using NetTopologySuite.IO.VectorTiles.Mapbox;

namespace HeatMap.Tiles.Test.Functional
{
    class Program
    {
        static void Main(string[] args)
        {
            var features = (new GeoJsonReader()).Read<FeatureCollection>(
                File.ReadAllText(@"/data/ANYWAYS Dropbox/data/bike-data-project/test-belgium.geojson"));

            Console.WriteLine("Adding features...");
            var heatmap = new HeatMap(14, 1024);
            foreach (var feature in features)
            {
                heatmap.Add(feature, 1);
            }
            
            // convert to a vector tile tree.d
            var tree = heatmap.ToVectorTiles(14, 14, i =>
            {
                if (i == 14) return 1024;

                return 256;
            });
            
            // write the tiles to disk as mvt.
            Console.WriteLine("Writing tiles...");
            tree.Write("tiles");
        }
    }
}
