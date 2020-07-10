using System.Collections;
using System.Collections.Generic;
using NetTopologySuite.IO.VectorTiles.Tiles;

namespace HeatMap.Tiles
{
    public class HeatMap : IEnumerable<(uint x, uint y)>
    {
        private readonly Dictionary<(uint x, uint y), HeatMapTile> _tiles = new Dictionary<(uint x, uint y), HeatMapTile>();

        public HeatMap(uint zoom, uint resolution = 256)
        {
            this.Zoom = zoom;
            this.Resolution = resolution;
        }
        
        public uint Zoom { get; }
        
        public uint Resolution { get; }

        private void ToTileCoordinates(long x, long y, out uint tileX, out uint tileY,
            out int xInTile, out int yInTile)
        {
            tileX = (uint) (x / this.Resolution);
            tileY = (uint) (y / this.Resolution);

            xInTile = (int)(x - (tileX * this.Resolution));
            yInTile = (int)(y - (tileY * this.Resolution));
        }

        internal HeatMapTile Get(ulong tileId)
        {
            var tile = new Tile(tileId);
            var tilePair = ((uint) tile.X, (uint) tile.Y);
            if (!_tiles.TryGetValue(tilePair, out var heatMapTile))
            {
                heatMapTile = new HeatMapTile(tileId, this.Resolution);
                _tiles[tilePair] = heatMapTile;
            }

            return heatMapTile;
        }
        
        public uint this[long x, long y]
        {
            get
            {
                ToTileCoordinates(x, y, out var tileX, out var tileY, out var xInTile, out var yInTile);
                if (!_tiles.TryGetValue((tileX, tileY), out var heatMapTile)) return 0;
                    
                return heatMapTile[xInTile, yInTile];
            }
            set
            {
                ToTileCoordinates(x, y, out var tileX, out var tileY, out var xInTile, out var yInTile);

                if (!_tiles.TryGetValue((tileX, tileY), out var heatMapTile))
                {
                    if (value == 0) return;
                    heatMapTile = new HeatMapTile(new Tile((int)tileX, (int)tileY, (int)this.Zoom).Id, this.Resolution);
                    _tiles[(tileX, tileY)] = heatMapTile;
                };
                
                heatMapTile[xInTile, yInTile] = value;
            }
        }

        public IEnumerator<(uint x, uint y)> GetEnumerator()
        {
            return _tiles.Keys.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}