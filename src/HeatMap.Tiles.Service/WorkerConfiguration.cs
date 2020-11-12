namespace HeatMap.Tiles.Service
{
    public class WorkerConfiguration
    {
        public string ConnectionString { get; set; }
        
        public int UserThreshold { get; set; }
        
        public int MaxUsers { get; set; }
        
        public int MaxContributions { get; set; }

        public string DataPath { get; set; }

        public string OutputPath { get; set; }

        public int RefreshTime { get; set; } = 1000;
    }
}