using System;
using System.IO;
using Reminiscence.Arrays;
using Reminiscence.IO;

namespace HeatMap.Tiles
{
    internal class HeatMapTile
    {
        private readonly uint _resolution;
        private readonly ArrayBase<uint> _data;

        internal HeatMapTile(ulong tileId, uint resolution = 1024)
        {
            TileId = tileId;
            _resolution = resolution;
            
            _data = new MemoryArray<uint>(_resolution * _resolution);
        }

        public uint Resolution => _resolution;

        public ulong TileId { get; }

        public uint this[int x, int y]
        {
            get
            {
                var i = (x * _resolution) + y;
                return _data[i];
            }
            set
            {
                var i = (x * _resolution) + y;
                _data[i] = value;
            }
        }
    }
}