using Microsoft.Extensions.Configuration;

namespace HeatMap.Tiles.Service
{
    internal static class IConfigurationExtensions
    {
        public static T GetValueOrDefault<T>(this IConfiguration configuration, string key, T defaultValue = default)
        {
            if (string.IsNullOrWhiteSpace(configuration[key])) return defaultValue;

            return configuration.GetValue<T>(key);
        }
    }
}