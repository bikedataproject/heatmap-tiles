using System.Collections.Generic;
using System.IO;
using NetTopologySuite.Geometries;
using Npgsql;

namespace HeatMap.Tiles.Test.Functional
{
    internal static class NpgsqlExtensions
    {
        public static async IAsyncEnumerable<Geometry> ReadContributionGeometries(this NpgsqlConnection cn)
        {
            cn.TypeMapper.UseNetTopologySuite();
            
            await using var cm = cn.CreateCommand();
            
            cm.CommandText = "select * from contributions";
            var reader = await cm.ExecuteReaderAsync();
            var pointsGeomId = reader.GetOrdinal("points_geom");
            while (reader.Read())
            {
                var data = reader[pointsGeomId];
                    
                if (!(data is Geometry geometry)) continue;

                yield return geometry;
            }
        }
    }
}