using System;
using System.Collections.Generic;
using System.IO;
using HeatMap.Tiles.IO;
using Reminiscence.Arrays;
using Reminiscence.IO;
using Reminiscence.IO.Streams;

namespace HeatMap.Tiles
{
    /// <summary>
    /// A tile in a heat map.
    /// </summary>
    public class HeatMapTile<T> : IDisposable
        where T: struct 
    {
        private const int BlockSize = 64;
        private const uint NoBlock = uint.MaxValue;
        private readonly ArrayBase<uint> _blockPointers;
        private readonly ArrayBase<T> _blocks;
        private readonly Stream _stream;

        /// <summary>
        /// Creates a new heat map.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="resolution">The resolution.</param>
        /// <exception cref="Exception"></exception>
        public HeatMapTile(Stream stream, uint resolution)
        {
            if (stream.Position != stream.Length) throw new Exception("This is not a new tile.");
            Resolution = resolution;
            _stream = stream;
            
            var length = Resolution * Resolution;
            var blocks = length / BlockSize;
            
            // write resolution.
            stream.WriteUInt32(Resolution);
            
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
            _blocks = ArrayBase<T>.CreateFor(new MemoryMapStream(new LimitedStream(stream)), 
                0, ArrayProfile.NoCache);
        }

        /// <summary>
        /// Creates a tile by reading the data.
        /// </summary>
        /// <param name="stream">The stream with the tile data.</param>
        /// <exception cref="Exception"></exception>
        public HeatMapTile(Stream stream)
        {
            if (stream.Position == stream.Length) throw new Exception("No data in stream.");
            _stream = stream;

            // read resolution.
            Resolution = stream.ReadUInt32();
            
            var length = Resolution * Resolution;
            var blocks = length / BlockSize;
            
            // create the mapped block pointers array.
            _blockPointers = ArrayBase<uint>.CreateFor(new MemoryMapStream(new LimitedStream(stream)), 
                blocks, ArrayProfile.NoCache);
            stream.Seek(4 + (blocks * 4), SeekOrigin.Begin);
            
            // create the mapped blocks array.
            var elementSize = MemoryMap.GetCreateAccessorFuncFor<T>()(new MemoryMapStream(new MemoryStream()), 64).ElementSize;
            var blocksSize = (stream.Length - stream.Position) / elementSize;
            _blocks = ArrayBase<T>.CreateFor(new MemoryMapStream(new LimitedStream(stream)), 
                blocksSize, ArrayProfile.NoCache);
        }

        /// <summary>
        /// Gets the tile resolution.
        /// </summary>
        public uint Resolution { get; }

        /// <summary>
        /// Enumerates all non-zero values.
        /// </summary>
        /// <returns>All non-zero values.</returns>
        public IEnumerable<(int x, int y, T value)> GetValues()
        {
            for (var b = 0; b < _blockPointers.Length; b++)
            {
                var blockPointer = _blockPointers[b];
                if (blockPointer == NoBlock) continue;

                for (var o = 0; o < BlockSize; o++)
                {
                    var val = _blocks[blockPointer + o];
                    if (val.Equals(default(T))) continue;

                    var pos = b * BlockSize + o;
                    var x = (int)(pos / this.Resolution);
                    var y = (int)(pos - (x * Resolution));
                    yield return (x, y, val);
                }
            }
        }

        /// <summary>
        /// Enumerates all non-zero values.
        /// </summary>
        /// <returns>All non-zero values.</returns>
        public void UpdateValues(Func<(int x, int y, T value), T> updateFunc)
        {
            for (var b = 0; b < _blockPointers.Length; b++)
            {
                var blockPointer = _blockPointers[b];
                if (blockPointer == NoBlock) continue;

                for (var o = 0; o < BlockSize; o++)
                {
                    var val = _blocks[blockPointer + o];
                    if (val.Equals(default(T))) continue;

                    var pos = b * BlockSize + o;
                    var x = (int)(pos / this.Resolution);
                    var y = (int)(pos - (x * Resolution));
                    
                    _blocks[blockPointer + o] = updateFunc((x, y, val));
                }
            }
        }

        /// <summary>
        /// Gets or sets the value at the given location.
        /// </summary>
        /// <param name="x">The x-coordinate.</param>
        /// <param name="y">The y-coordinate.</param>
        public T this[int x, int y]
        {
            get
            {
                var pos = (x * Resolution) + y;
                var block = pos / BlockSize;
                var blockPointer = _blockPointers[block];
                if (blockPointer == NoBlock) return default(T);

                var blockOffset = pos - (block * BlockSize);
                var i = blockPointer + blockOffset;
                // if (i >= _blocks.Length) return default(T);
                return _blocks[i];
            }
            set
            {
                var pos = (x * Resolution) + y;
                var block = pos / BlockSize;
                var blockPointer = _blockPointers[block];
                if (blockPointer == NoBlock)
                {
                    if (value.Equals(default(T))) return;

                    blockPointer = (uint)_blocks.Length;
                    _blocks.Resize(_blocks.Length + BlockSize);
                    for (var i = 0; i < BlockSize; i++)
                    {
                        _blocks[blockPointer + i] = default;
                    }

                    _blockPointers[block] = blockPointer;
                }
                
                var blockOffset = pos - (block * BlockSize);
                _blocks[blockPointer + blockOffset] = value;
            }
        }
        
        /// <summary>
        /// Disposes and flushes.
        /// </summary>
        public void Dispose()
        {
            _stream.Dispose();
            _blockPointers?.Dispose();
            _blocks?.Dispose();
        }
    }
}