using System.IO;
using Reminiscence.Arrays;
using Reminiscence.IO;
using Reminiscence.IO.Streams;

namespace HeatMap.Tiles
{
    internal class HeatMapTile
    {
        private readonly ArrayBase<uint> _data;

        internal HeatMapTile(HeatMap heatMap, Stream stream)
        {
            HeatMap = heatMap;
            
            _data = ArrayBase<uint>.CreateFor(new MemoryMapStream(new LimitedStream(stream)), HeatMap.Resolution * HeatMap.Resolution,
                ArrayProfile.NoCache);
            if (stream.Position == stream.Length)
            {
                for (var i = 0; i < heatMap.Resolution * heatMap.Resolution; i++)
                {
                    _data[i] = 0;
                }
            }
        }

        public HeatMap HeatMap { get; }


        public uint SumRange(long x, long y, long step)
        {
            uint sum = 0;
            var xOffset = (x * HeatMap.Resolution);
            for (var i = xOffset; i < xOffset + (step * HeatMap.Resolution); 
                i += HeatMap.Resolution)
            {
                for (var j = y ; j < y + step; j++)
                {
                    sum += _data[i + j];
                }
            }

            return sum;
        }

        public uint this[int x, int y]
        {
            get
            {
                var i = (x * HeatMap.Resolution) + y;
                return _data[i];
            }
            set
            {
                var i = (x * HeatMap.Resolution) + y;
                _data[i] = value;
            }
        }

        internal static long TileSizeInBytes(uint resolution)
        {
            return 4 + 8 + 4 + (resolution * resolution * 4);
        }
    }
}