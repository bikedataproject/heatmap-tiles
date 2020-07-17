using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HeatMap.Tiles.IO;
using Reminiscence.Arrays;
using Reminiscence.IO;
using Reminiscence.IO.Streams;

namespace HeatMap.Tiles
{
    /// <summary>
    /// A stream-backed heatmap.
    /// </summary>
    public class HeatMap : IEnumerable<uint>
    {
        private readonly long _tileBlockSize = 1024;
        
        private readonly ArrayBase<long> _tileBlockPointers; // pointers to the tile pointers block per block of tiles. 
        private readonly ArrayBase<long>?[] _tilePointers; // pointers to the tiles in the block.
        private readonly Dictionary<uint, HeatMapTile> _tiles = new Dictionary<uint, HeatMapTile>();
        private readonly long _streamPosition;
        private readonly Stream _stream;

        /// <summary>
        /// Creates a brand new heatmap starting at the current stream position.
        /// </summary>
        /// <param name="stream">The backing stream.</param>
        /// <param name="zoom">The zoom level.</param>
        /// <param name="resolution">The resolution.</param>
        public HeatMap(Stream stream, int zoom, uint resolution = 256)
        {
            _streamPosition = stream.Position;
            _stream = stream;
            
            this.Zoom = zoom;
            this.Resolution = resolution;
            
            // initialize stream.
            _stream.WriteInt32(1); // version.
            _stream.WriteInt32(zoom);
            _stream.WriteUInt32(resolution);
            
            // create tile blocks and initialize to 0.
            var maxLocalId = TileStatic.MaxLocalId(zoom);
            var blocks = maxLocalId / _tileBlockSize;
            _tileBlockPointers = ArrayBase<long>.CreateFor(new MemoryMapStream(new LimitedStream(_stream)), blocks, ArrayProfile.NoCache);
            for (var b = 0; b < blocks; b++)
            {
                _tileBlockPointers[b] = 0;
            }
            _tilePointers = new ArrayBase<long>?[blocks];
        }

        /// <summary>
        /// Loads a heatmap from the given stream starting at the current stream position.
        /// </summary>
        /// <param name="stream"></param>
        public HeatMap(Stream stream)
        {
            _streamPosition = stream.Position;
            _stream = stream;
            
            // read and verify version number.
            var version = stream.ReadInt32();
            if (version != 1) throw new Exception("Invalid version number.");
            
            // read zoom and resolution.
            this.Zoom = stream.ReadInt32();
            this.Resolution = stream.ReadUInt32();
            
            // create tile block pointers array.
            var maxLocalId = TileStatic.MaxLocalId(this.Zoom);
            var blocks = maxLocalId / _tileBlockSize;
            _tileBlockPointers = ArrayBase<long>.CreateFor(new MemoryMapStream(new LimitedStream(stream)), blocks, ArrayProfile.NoCache);
            _tilePointers = new ArrayBase<long>?[blocks];
        }
        
        public int Zoom { get; }
        
        public uint Resolution { get; }

        private bool TryGetTileBlock(long block, out ArrayBase<long> blockData)
        {
            blockData = _tilePointers[block];
            if (blockData != null) return true;
            
            // block data not there yet, try to load it.
            var blockPointer = _tileBlockPointers[block];
            if (blockPointer == 0)
            {
                blockData = null;
                return false;
            }
            blockPointer--;

            _stream.Seek(_streamPosition + blockPointer, SeekOrigin.Begin);
            blockData =
                ArrayBase<long>.CreateFor(new MemoryMapStream(new LimitedStream(_stream)), _tileBlockSize, ArrayProfile.NoCache);
            _tilePointers[block] = blockData;
            return true;
        }

        private ArrayBase<long> CreateBlock(long block)
        {
            // add a new block at the end of the stream.
            _stream.Seek(0, SeekOrigin.End);
            var blockPointer = _stream.Position - _streamPosition;
            var blockData = ArrayBase<long>.CreateFor(new MemoryMapStream(new LimitedStream(_stream)), _tileBlockSize, ArrayProfile.NoCache);
            for (var i = 0; i < blockData.Length; i++)
            {
                blockData[i] = 0;
            }
            
            // store the pointer/block.
            _tileBlockPointers[block] = blockPointer + 1;
            _tilePointers[block] = blockData;
            return blockData;
        }

        private bool TryGetTile(uint tile, out HeatMapTile heatMapTile)
        {
            if (_tiles.TryGetValue(tile, out heatMapTile)) return true;
            
            // tile not there yet, try to load tile.
            var block = tile / _tileBlockSize;
            if (!this.TryGetTileBlock(block, out var blockData))
            {
                return false;
            }
            
            var offset = tile - (block * _tileBlockSize);
            var tilePointer = blockData[offset];
            if (tilePointer == 0)
            {
                return false;
            }
            tilePointer--;

            _stream.Seek(_streamPosition + tilePointer, SeekOrigin.Begin);
            heatMapTile = new HeatMapTile(this, _stream);
            _tiles[tile] = heatMapTile;
            return true;
        }

        private HeatMapTile CreateTile(uint tile)
        {
            // tile not there yet, try to load tile.
            var block = tile / _tileBlockSize;
            if (!this.TryGetTileBlock(block, out var blockData))
            {
                blockData = this.CreateBlock(block);
            }
            
            // create tile.
            _stream.Seek(0, SeekOrigin.End);
            var tilePointer = _stream.Position - _streamPosition;
            var heatmapTile = new HeatMapTile(this, _stream);
            _tiles[tile] = heatmapTile;
            
            // store pointer.
            var offset = tile - (block * _tileBlockSize);
            blockData[offset] = tilePointer + 1;
            return heatmapTile;
        }

        internal HeatMapTile Get(uint tile)
        {
            if (!this.TryGetTile(tile, out var heatMapTile))
            {
                heatMapTile = this.CreateTile(tile);
            }

            return heatMapTile;
        }

        private void ToTileCoordinates(long x, long y, out uint tileX, out uint tileY,
            out int xInTile, out int yInTile)
        {
            tileX = (uint) (x / this.Resolution);
            tileY = (uint) (y / this.Resolution);

            xInTile = (int)(x - (tileX * this.Resolution));
            yInTile = (int)(y - (tileY * this.Resolution));
        }

        private (uint tileId, HeatMapTile? heatMapTile) _latest = (uint.MaxValue, null);

        public uint SumRange(long x, long y, long step)
        {
            if (step == 0) return 0;
            if (step == 1) return this[x, y];
            
            var tileX = (uint) (x / this.Resolution);
            var tileY = (uint) (y / this.Resolution);

            var xInTile = (int)(x - (tileX * this.Resolution));
            var yInTile = (int)(y - (tileY * this.Resolution));

            if (xInTile + step < this.Resolution &&
                yInTile + step < this.Resolution)
            {
                var tileId = TileStatic.ToLocalId(tileX, tileY, this.Zoom);
                
                if (_latest.tileId != tileId)
                {
                    if (!this.TryGetTile(tileId, out var heatMapTile))
                    {
                        _latest = (tileId, null);
                    }
                    else
                    {
                        _latest = (tileId, heatMapTile);
                    }
                }
                
                if (_latest.heatMapTile == null) return 0;

                return _latest.heatMapTile.SumRange(xInTile, yInTile, step);
            }
            else
            {
                step /= 2;
                
                var sum = SumRange(x, y, step);
                sum += SumRange(x + step, y, step);
                sum += SumRange(x, y + step, step);
                sum += SumRange(x + step, y + step, step);
                return sum;
            }
        }
        
        public uint this[long x, long y]
        {
            get
            {
                ToTileCoordinates(x, y, out var tileX, out var tileY, out var xInTile, out var yInTile);
                var tileId = TileStatic.ToLocalId(tileX, tileY, this.Zoom);

                if (_latest.tileId != tileId)
                {
                    if (!this.TryGetTile(tileId, out var heatMapTile))
                    {
                        _latest = (tileId, null);
                    }
                    else
                    {
                        _latest = (tileId, heatMapTile);
                    }
                }

                if (_latest.heatMapTile == null) return 0;
                
                return _latest.heatMapTile[xInTile, yInTile];
            }
            set
            {
                ToTileCoordinates(x, y, out var tileX, out var tileY, out var xInTile, out var yInTile);
                var tileId = TileStatic.ToLocalId(tileX, tileY, this.Zoom);
                
                if (_latest.tileId != tileId)
                {
                    if (!this.TryGetTile(tileId, out var heatMapTile))
                    {
                        _latest = (tileId, null);
                    }
                    else
                    {
                        _latest = (tileId, heatMapTile);
                    }
                }

                if (_latest.heatMapTile == null)
                {
                    if (value == 0) return;
                    
                    _latest = (tileId, this.CreateTile(tileId));
                }
                
                _latest.heatMapTile[xInTile, yInTile] = value;
            }
        }

        public IEnumerator<uint> GetEnumerator()
        {
            for (var b = 0L; b < _tileBlockPointers.Length; b++)
            {
                if (!this.TryGetTileBlock(b, out var blockData)) continue;

                var blockStart = _tileBlockSize * b;
                for (var t = 0; t < blockData.Length; t++)
                {
                    var blockPointer = blockData[t];
                    if (blockPointer == 0) continue;
                    
                    // tile not empty!
                    yield return (uint) (blockStart + t);
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}