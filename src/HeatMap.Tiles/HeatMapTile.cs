using System;
using System.IO;
using HeatMap.Tiles.IO;
using Reminiscence.Arrays;
using Reminiscence.IO;
using Reminiscence.IO.Streams;

namespace HeatMap.Tiles
{
    public class HeatMapTile
    {
        private const int BlockSize = 64;
        private const uint NoBlock = uint.MaxValue;
        private readonly ArrayBase<uint> _blockPointers;
        private readonly ArrayBase<uint> _blocks;
        private readonly uint _resolution;

        public HeatMapTile(Stream stream, uint resolution)
        {
            if (stream.Position != stream.Length) throw new Exception("This is not a new tile.");
            _resolution = resolution;
            
            var length = _resolution * _resolution;
            var blocks = length / BlockSize;
            
            // write resolution.
            stream.WriteUInt32(_resolution);
            
            // create the mapped block pointers array and initialize with zeros.
            _blockPointers = ArrayBase<uint>.CreateFor(new MemoryMapStream(new LimitedStream(stream)), 
                blocks, ArrayProfile.NoCache);
            stream.Seek(4 + (blocks * 4), SeekOrigin.Begin);
            for (var i = 0; i < _blockPointers.Length; i++)
            {
                _blockPointers[i] = NoBlock;
            }
            stream.Seek(4 + (blocks * 4), SeekOrigin.Begin);
            
            // create the mapped blocks array at the end.
            // no need to initialize, it is empty.
            _blocks = ArrayBase<uint>.CreateFor(new MemoryMapStream(new LimitedStream(stream)), 
                0, ArrayProfile.NoCache);
        }

        public HeatMapTile(Stream stream)
        {
            if (stream.Position == stream.Length) throw new Exception("No data in stream.");

            // read resolution.
            _resolution = stream.ReadUInt32();
            
            var length = _resolution * _resolution;
            var blocks = length / BlockSize;
            
            // create the mapped block pointers array.
            _blockPointers = ArrayBase<uint>.CreateFor(new MemoryMapStream(new LimitedStream(stream)), 
                blocks, ArrayProfile.NoCache);
            stream.Seek(4 + (blocks * 4), SeekOrigin.Begin);
            
            // create the mapped blocks array.
            var blocksSize = (stream.Length - stream.Position) / 4;
            _blocks = ArrayBase<uint>.CreateFor(new MemoryMapStream(new LimitedStream(stream)), 
                blocksSize, ArrayProfile.NoCache);
        }

        public uint Resolution => _resolution;

        public uint this[int x, int y]
        {
            get
            {
                var pos = (x * _resolution) + y;
                var block = pos / BlockSize;
                var blockPointer = _blockPointers[block];
                if (blockPointer == NoBlock) return 0;

                var blockOffset = pos - (block * BlockSize);
                return _blocks[blockPointer + blockOffset];
            }
            set
            {
                var pos = (x * _resolution) + y;
                var block = pos / BlockSize;
                var blockPointer = _blockPointers[block];
                if (blockPointer == NoBlock)
                {
                    if (value == 0) return;

                    blockPointer = (uint)_blocks.Length;
                    _blocks.Resize(_blocks.Length + BlockSize);
                    for (var i = 0; i < BlockSize; i++)
                    {
                        _blocks[blockPointer + i] = 0;
                    }

                    _blockPointers[block] = blockPointer;
                }
                
                var blockOffset = pos - (block * BlockSize);
                _blocks[blockPointer + blockOffset] = value;
            }
        }
    }
}