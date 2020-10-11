using System;
using System.Collections;
using System.Collections.Generic;
using Reminiscence.Arrays;

namespace HeatMap.Tiles.Diffs
{
    /// <summary>
    /// An in memory heat map.
    /// </summary>
    public class HeatMapDiff : IEnumerable<uint>
    {
        private const uint NoTile = uint.MaxValue;
        private readonly uint _tileResolution;
        private readonly Dictionary<uint, uint> _tilePointers;
        private readonly MemoryArray<uint> _tiles;

        //private (uint first, uint last)? _range;
        private long _tilesLength = 0;

        /// <summary>
        /// Creates a brand new heat map diff starting at the current stream position.
        /// </summary>
        /// <param name="zoom">The zoom level.</param>
        /// <param name="resolution">The resolution.</param>
        public HeatMapDiff(int zoom, uint resolution)
        {
            this.Zoom = zoom;
            this.Resolution = resolution;

            _tileResolution = (uint) (1 << zoom);
            _tilePointers = new Dictionary<uint, uint>();
            _tiles = new MemoryArray<uint>(0);
            _tilesLength = 0;
        }

        private HeatMapDiff? _oneLevelUp;

        internal HeatMapDiff CreateOneLevelUp()
        {
            if (_oneLevelUp == null)
            {
                _oneLevelUp = new HeatMapDiff(this.Zoom - 1, this.Resolution);
            }
            else
            {
                _oneLevelUp.Clear();
            }

            return _oneLevelUp;
        }
        
        public void Clear()
        {
            _tilesLength = 0;
            _tiles.Resize(0);
            _tilePointers.Clear();
        }

        private uint GetTileBlockFor(long x, long y, out uint offset)
        {
            var tileX = (uint) (x / this.Resolution);
            var tileY = (uint) (y / this.Resolution);

            var result = _tileResolution * tileY + tileX;

            tileX = (uint)(x - (tileX * this.Resolution));
            tileY = (uint)(y - (tileY * this.Resolution));

            offset = (this.Resolution * tileY + tileX);
            return result;
        }
        
        public int Zoom { get; }
        
        public uint Resolution { get; }

        public uint this[long x, long y]
        {
            get
            {
                var tile = GetTileBlockFor(x, y, out var offset);
                if (!_tilePointers.TryGetValue(tile, out var tilePointer)) return 0;
                if (tilePointer == NoTile) return 0;
                
                return _tiles[tilePointer + offset];
            }
            set
            {
                var tile = GetTileBlockFor(x, y, out var offset);
                if (!_tilePointers.TryGetValue(tile, out var tilePointer) ||
                    tilePointer == NoTile)
                {
                    if (value == 0) return;

                    tilePointer = (uint)_tilesLength;
                    var newSize = _tilesLength + (this.Resolution * this.Resolution);
                    if (_tiles.Length < newSize) _tiles.Resize(newSize);
                    for (var i = tilePointer; i < newSize; i++)
                    {
                        _tiles[i] = 0;
                    }
                    _tilesLength = newSize;
                    _tilePointers[tile] = tilePointer;
                    GetTileBlockFor(x, y, out offset);
                }
                
                _tiles[tilePointer + offset] = value;
            }
        }

        public void RemoveAll(Func<uint, bool> toRemove)
        {
            foreach (var (tileId, tilePointer) in _tilePointers)
            {
                if (tilePointer == NoTile) continue;
                
                if (!toRemove(tileId)) continue;
                _tilePointers[tileId] = NoTile;
            }
        }

        public IEnumerator<uint> GetEnumerator()
        {
            foreach (var (tileId, tilePointer) in _tilePointers)
            {
                if (tilePointer == NoTile) continue;

                yield return tileId;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}