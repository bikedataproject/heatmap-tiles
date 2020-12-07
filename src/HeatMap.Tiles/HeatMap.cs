using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace HeatMap.Tiles
{
    /// <summary>
    /// A file-backed heat map.
    /// </summary>
    public class HeatMap<T> : IDisposable
        where T : struct
    {
        private readonly string _path;
        private readonly uint _resolution;
        private readonly Dictionary<(uint x, uint y, int z), HeatMapTile<T>> _tiles = new Dictionary<(uint x, uint y, int z), HeatMapTile<T>>();

        /// <summary>
        /// Creates a new heat map using the given path for storage.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="resolution">The resolution.</param>
        public HeatMap(string path, uint resolution = 1024)
        {
            _resolution = resolution;
            _path = path;
        }

        /// <summary>
        /// Gets the resolution.
        /// </summary>
        public uint Resolution => _resolution;

        /// <summary>
        /// Enumerates all the tiles with data.
        /// </summary>
        /// <returns>The tiles with data.</returns>
        public IEnumerable<(uint x, uint y, int z)> GetTiles()
        {
            var zoomDirs = Directory.EnumerateDirectories(_path);
            foreach (var zoomDir in zoomDirs)
            {
                var zoomDirInfo = new DirectoryInfo(zoomDir);
                if (!int.TryParse(zoomDirInfo.Name, out var z)) continue;

                var xDirs = Directory.EnumerateDirectories(zoomDir);
                foreach (var xDir in xDirs)
                {
                    var xDirInfo = new DirectoryInfo(xDir);
                    if (!uint.TryParse(xDirInfo.Name, out var x)) continue;

                    var yFiles = Directory.EnumerateFiles(xDir, "*.mvt");
                    foreach (var yFile in yFiles)
                    {
                        var yFileInfo = new FileInfo(yFile);
                        if (!uint.TryParse(yFileInfo.Name, out var y)) continue;

                        yield return (x, y, z);
                    }
                }
            }
        }

        /// <summary>
        /// Gets or creates a tile.
        /// </summary>
        /// <param name="x">The x-coordinate.</param>
        /// <param name="y">The y-coordinate.</param>
        /// <param name="z">The zoom.</param>
        public HeatMapTile<T> this[uint x, uint y, int z]
        {
            get
            {
                if (_tiles.TryGetValue((x, y, z), out var tile)) return tile;
                
                tile = GetOrCreate(x, y, z);
                _tiles[(x, y, z)] = tile;
                return tile;
            }
        }
        
        /// <summary>
        /// Tries to get a tile.
        /// </summary>
        /// <param name="tile">The tile.</param>
        /// <param name="heatMapTile">The heat map tile.</param>
        /// <returns>True if the tile exists, false otherwise.</returns>
        public bool TryGetTile((uint x, uint y, int z) tile, [NotNullWhen(returnValue: true)] out HeatMapTile<T>? heatMapTile)
        {
            if (_tiles.TryGetValue(tile, out heatMapTile)) return true;
            
            var file = FileName(tile.x, tile.y, tile.z);
            if (File.Exists(file))
            {
                heatMapTile = this[tile.x, tile.y, tile.z];
                return true;
            }

            heatMapTile = null;
            return false;
        }

        /// <summary>
        /// Try to remove a tile.
        /// </summary>
        /// <param name="tile">The tile to remove.</param>
        /// <returns>True if the tile was found and removed.</returns>
        public bool TryRemoveTile((uint x, uint y, int z) tile)
        {
            if (_tiles.TryGetValue(tile, out var heatMapTile))
            {
                heatMapTile.Dispose();
                _tiles.Remove(tile);
            }

            var file = FileName(tile.x, tile.y, tile.z);
            if (!File.Exists(file)) return false;
            
            File.Delete(file);
            return true;
        }

        /// <summary>
        /// Flush and unload all tiles.
        /// </summary>
        public void FlushAndUnload()
        {
            foreach (var (_, tile) in _tiles)
            {
                tile.Dispose();
            }
            _tiles.Clear();
        }

        /// <summary>
        /// Disposes of all resources. Flushes tiles. 
        /// </summary>
        public void Dispose()
        {
            foreach (var (_, tile) in _tiles)
            {
                tile.Dispose();
            }
        }

        private string FileName(uint x, uint y, int z)
        {
            return Path.Combine(_path, $"{z}", $"{x}", $"{y}.heatmap");
        }
        
        private HeatMapTile<T> GetOrCreate(uint x, uint y, int z)
        {            
            var file = FileName(x, y, z);

            var zPath = Path.Combine(_path, $"{z}");
            if (!Directory.Exists(zPath)) Directory.CreateDirectory(zPath);
            var xPath = Path.Combine(zPath, $"{x}");
            if (!Directory.Exists(xPath)) Directory.CreateDirectory(xPath);

            if (File.Exists(file))
            {
                var stream = File.Open(file, FileMode.Open);
                return new HeatMapTile<T>(stream);
            }
            else
            {
                var stream = File.Open(file, FileMode.Create);
                return new HeatMapTile<T>(stream, _resolution);
            }
        }
    }
}