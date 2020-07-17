using System.IO;
using Reminiscence.Arrays;
using Reminiscence.IO;
using Reminiscence.IO.Streams;

namespace HeatMap.Tiles.Diffs
{
    internal class HeatMapDiffTile
    {
        private readonly ArrayBase<uint> _data;

        internal HeatMapDiffTile(HeatMapDiff heatMapDiff, Stream stream)
        {
            HeatMapDiff = heatMapDiff;
            
            _data = ArrayBase<uint>.CreateFor(new MemoryMapStream(new LimitedStream(stream)), HeatMapDiff.Resolution * HeatMapDiff.Resolution,
                ArrayProfile.NoCache);
            if (stream.Position == stream.Length)
            {
                for (var i = 0; i < heatMapDiff.Resolution * heatMapDiff.Resolution; i++)
                {
                    _data[i] = 0;
                }
            }
        }

        public HeatMapDiff HeatMapDiff { get; }


        public uint SumRange(long x, long y, long step)
        {
            uint sum = 0;
            var xOffset = (x * HeatMapDiff.Resolution);
            for (var i = xOffset; i < xOffset + (step * HeatMapDiff.Resolution); 
                i += HeatMapDiff.Resolution)
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
                var i = (x * HeatMapDiff.Resolution) + y;
                return _data[i];
            }
            set
            {
                var i = (x * HeatMapDiff.Resolution) + y;
                _data[i] = value;
            }
        }

        internal static long TileSizeInBytes(uint resolution)
        {
            return 4 + 8 + 4 + (resolution * resolution * 4);
        }
    }
}