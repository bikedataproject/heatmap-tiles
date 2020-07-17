using System.Collections.Generic;
using System.IO;

namespace HeatMap.Tiles
{
    public class HeatMap
    {
        private readonly string _path;
        private readonly uint _resolution;
        private readonly Dictionary<(uint x, uint y, int z), HeatMapTile> _tiles = new Dictionary<(uint x, uint y, int z), HeatMapTile>();

        public HeatMap(string path, uint resolution = 1024)
        {
            _resolution = resolution;
            _path = path;
        }

        private string FileName(uint x, uint y, int z)
        {
            return Path.Combine(_path, $"{z}", $"{x}", $"{y}.heatmap");
        }
        
        private HeatMapTile GetOrCreate(uint x, uint y, int z)
        {            
            var file = FileName(x, y, z);

            var zPath = Path.Combine(_path, $"{z}");
            if (!Directory.Exists(zPath)) Directory.CreateDirectory(zPath);
            var xPath = Path.Combine(zPath, $"{x}");
            if (!Directory.Exists(xPath)) Directory.CreateDirectory(xPath);

            if (File.Exists(file))
            {
                var stream = File.Open(file, FileMode.Open);
                return new HeatMapTile(stream);
            }
            else
            {
                var stream = File.Open(file, FileMode.Create);
                return new HeatMapTile(stream, _resolution);
            }
        }
        
        public HeatMapTile this[uint x, uint y, int z]
        {
            get
            {
                if (_tiles.TryGetValue((x, y, z), out var tile)) return tile;
                
                tile = GetOrCreate(x, y, z);
                _tiles[(x, y, z)] = tile;
                return tile;
            }
        }
    }
}