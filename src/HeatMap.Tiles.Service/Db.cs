using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;
using Npgsql;

namespace HeatMap.Tiles.Service
{
    internal static class Db
    {
        public static async Task<long?> GetEarliestContributionId(this NpgsqlConnection connection)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "select c.\"ContributionId\" from public.\"Contributions\" c order by c.\"ContributionId\" asc limit 1";
            var result = cmd.ExecuteScalar();

            return result switch
            {
                long contributionId => contributionId,
                int contributionIdInt => contributionIdInt,
                _ => null
            };
        }
        
        public static async Task<long?> GetLatestContributionId(this NpgsqlConnection connection)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "select c.\"ContributionId\" from public.\"Contributions\" c order by c.\"ContributionId\" desc limit 1";
            var result = cmd.ExecuteScalar();

            return result switch
            {
                long contributionId => contributionId,
                int contributionIdInt => contributionIdInt,
                _ => null
            };
        }

        public static async IAsyncEnumerable<(long contributionId, Geometry geometry, string userId)> GetDataForWindow(
            this NpgsqlConnection connection, long from, long to)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "select c.\"ContributionId\",c.\"PointsGeom\", u.\"UserIdentifier\" "+
                "from public.\"Contributions\" c "+
                "inner join public.\"UserContributions\" uc "+
                "on uc.\"ContributionId\" = c.\"ContributionId\" "+
                "inner join public.\"Users\" u "+
                "on uc.\"UserId\" = u.\"Id\" "+
                "where c.\"ContributionId\" > @from and c.\"ContributionId\" <= @to "+
                "order by c.\"ContributionId\"" ;
            cmd.Parameters.AddWithValue("from", from);
            cmd.Parameters.AddWithValue("to", to);

            var reader = await cmd.ExecuteReaderAsync();
            while (reader.Read())
            {
                var contributionId = reader.GetInt64(0);
                var geometryResult = reader[1];
                if (!(geometryResult is Geometry geometry)) continue;
                var userId = reader.GetGuid(2).ToString();

                yield return (contributionId, geometry, userId);
            }
        }
    }
}