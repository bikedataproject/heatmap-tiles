namespace HeatMap.Tiles
{
    public static class Encoder
    {
        public static (uint userCount, uint tripCount) Decode(ulong heatMapValue)
        {
            return ((uint)(heatMapValue >> 32), (uint)(heatMapValue & uint.MaxValue));
        }
        
        public static ulong Encode(uint userCount, uint tripCount)
        {
            return (ulong)userCount << 32 | tripCount;
        }
    }
}