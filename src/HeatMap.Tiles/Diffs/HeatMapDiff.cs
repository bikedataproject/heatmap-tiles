using System.Collections;
using System.Collections.Generic;
using Reminiscence.Arrays;

namespace HeatMap.Tiles.Diffs
{
    /// <summary>
    /// A stream-backed heat map.
    /// </summary>
    public class HeatMapDiff : IEnumerable<uint>
    {
        private const uint NoTile = uint.MaxValue;
        private readonly uint _tileResolution;
        private readonly uint[] _tilePointers;
        private readonly MemoryArray<uint> _tiles;

        private (uint first, uint last)? _range;

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
            var tileCount = _tileResolution * _tileResolution;
            
            _tilePointers = new uint[tileCount];
            for (var i = 0; i < _tilePointers.Length; i++)
            {
                _tilePointers[i] = NoTile;
            }
            _tiles = new MemoryArray<uint>(0);
            _range = null;
        }

        private void UpdateRange(uint tile)
        {
            if (_range == null)
            {
                _range = (tile, tile);
            }
            else
            {
                _range = (_range.Value.first < tile ? _range.Value.first : tile,
                    _range.Value.last > tile ? _range.Value.last : tile);
            }
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
                var tilePointer = _tilePointers[tile];
                if (tilePointer == NoTile) return 0;
                
                return _tiles[tilePointer + offset];
            }
            set
            {
                var tile = GetTileBlockFor(x, y, out var offset);
                var tilePointer = _tilePointers[tile];
                if (tilePointer == NoTile)
                {
                    if (value == 0) return;

                    tilePointer = (uint)_tiles.Length;
                    _tiles.Resize(_tiles.Length + (this.Resolution * this.Resolution));
                    _tilePointers[tile] = tilePointer;
                    UpdateRange(tile);
                    GetTileBlockFor(x, y, out offset);
                }
                
                _tiles[tilePointer + offset] = value;
            }
        }

        public IEnumerator<uint> GetEnumerator()
        {
            if (_range == null) yield break;
            for (var tileId = _range.Value.first; tileId <= _range.Value.last; tileId++)
            {
                var tilePointer = _tilePointers[tileId];
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