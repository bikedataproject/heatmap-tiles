using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;
using Npgsql;

namespace HeatMap.Tiles.Service
{
    internal static class Db
    {
        public static async Task<DateTimeOffset?> GetEarliestContributionTimeStamp(this NpgsqlConnection connection)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "select created_at from contributions order by created_at asc limit 1";
            var result = cmd.ExecuteScalar();

            if (!(result is DateTime dateTime)) return null;
            
            return new DateTimeOffset(dateTime.ToUniversalTime());
        }
        
        public static async Task<DateTimeOffset?> GetLatestContributionTimeStamp(this NpgsqlConnection connection)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "select created_at from contributions order by created_at desc limit 1";
            var result = cmd.ExecuteScalar();

            if (!(result is DateTime dateTime)) return null;
            
            return new DateTimeOffset(dateTime.ToUniversalTime());
        }

        public static async IAsyncEnumerable<(DateTimeOffset createdAt, Geometry geometry)> GetDataForTimeWindow(
            this NpgsqlConnection connection,
            DateTimeOffset from, DateTimeOffset to)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "select created_at, points_geom from contributions where created_at > @from and created_at <= @to order by created_at asc";
            cmd.Parameters.AddWithValue("from", from);
            cmd.Parameters.AddWithValue("to", to);

            var reader = await cmd.ExecuteReaderAsync();
            while (reader.Read())
            {
                var createdAt = new DateTimeOffset(reader.GetDateTime(0).ToUniversalTime());
                var geometryResult = reader[1];
                if (!(geometryResult is Geometry geometry)) continue;

                yield return (createdAt, geometry);
            }
        }
    }
}