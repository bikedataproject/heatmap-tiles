using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

namespace HeatMap.Tiles
{
    internal static class TileStatic
    {
        public static (uint x, uint y) ToTile(int zoom, uint tileId)
        {
            var xMax = (ulong) (1 << zoom);

            return ((uint) (tileId % xMax), (uint) (tileId / xMax));
        }

        public static uint ToLocalId((uint x, uint y) tile, int zoom)
        {
            return ToLocalId(tile.x, tile.y, zoom);
        }

        public static uint ToLocalId(uint x, uint y, int zoom)
        {
            var xMax = (1 << (int) zoom);
            return (uint)(y * xMax + x);
        }

        public static uint MaxLocalId(int zoom)
        {
            var xMax = (uint)(1 << (int) zoom);
            return (xMax * xMax + xMax);
        }

        public static ((double longitude, double latitude) topLeft, (double longitude, double latitude) bottomRight) Box(int zoom, uint tileId)
        {
            var tile = ToTile(zoom, tileId);
            
            var n = Math.PI - ((2.0 * Math.PI * tile.y) / Math.Pow(2.0, zoom));
            var left = ((tile.x / Math.Pow(2.0, zoom) * 360.0) - 180.0);
            var top = (180.0 / Math.PI * Math.Atan(Math.Sinh(n)));

            n = Math.PI - ((2.0 * Math.PI * (tile.y + 1)) / Math.Pow(2.0, zoom));
            var right = ((tile.x + 1) / Math.Pow(2.0, zoom) * 360.0) - 180.0;
            var bottom = (180.0 / Math.PI * Math.Atan(Math.Sinh(n)));

            return ((left, top), (right, bottom));
        }

        public static (double x, double y) SubCoordinates(int zoom, uint tileId, double longitude, double latitude)
        {
            // TODO: this may not be correct the tile coordinate north down and these are defined south up.
            var (x, y) = ToTile(zoom, tileId);
            var box = Box(zoom, tileId);
            var left = box.topLeft.longitude;
            var right = box.bottomRight.longitude;
            var bottom = box.bottomRight.latitude;
            var top = box.topLeft.latitude;

            var leftOffset = longitude - left;
            var bottomOffset = latitude - bottom;

            return (x + (leftOffset / (right - left)),
                y + (bottomOffset / (top - bottom)));
        }

        public static bool IsDirectNeighbour(int zoom, uint t1, uint t2)
        {
            var tile1 = ToTile(zoom, t1);
            var tile2 = ToTile(zoom, t2);
            
            if (tile1.x == tile2.x)
            {
                return (tile1.y == tile2.y + 1) ||
                       (tile1.y == tile2.y - 1);
            }
            else if (tile1.y == tile2.y)
            {
                return (tile1.x == tile2.x + 1) ||
                       (tile1.x == tile2.x - 1);
            }

            return false;
        }

        public static (int x, int y, uint tileId) ToLocalTileCoordinates(int zoom, (double longitude, double latitude) location,
            int resolution)
        {
            var tileId = TileStatic.WorldTileLocalId(location, zoom);
            
            var tileLocation = ToLocalTileCoordinates(zoom, tileId, location.longitude, location.latitude, resolution);

            return (tileLocation.x, tileLocation.y, tileId);
        }

        public static (int x, int y) ToLocalTileCoordinates(int zoom, uint tileId, (double longitude, double latitude) location,
            int resolution)
        {
            return ToLocalTileCoordinates(zoom, tileId, location.longitude, location.latitude, resolution);
        }

        public static (int x, int y) ToLocalTileCoordinates(int zoom, uint tileId, double longitude, double latitude, int resolution)
        {
            var tile = ToTile(zoom, tileId);
            
            var n = Math.PI - ((2.0 * Math.PI * tile.y) / Math.Pow(2.0, zoom));
            var left = ((tile.x / Math.Pow(2.0, zoom) * 360.0) - 180.0);
            var top = (180.0 / Math.PI * Math.Atan(Math.Sinh(n)));

            n = Math.PI - ((2.0 * Math.PI * (tile.y + 1)) / Math.Pow(2.0, zoom));
            var right = ((tile.x + 1) / Math.Pow(2.0, zoom) * 360.0) - 180.0;
            var bottom = (180.0 / Math.PI * Math.Atan(Math.Sinh(n)));
            
            var latStep = (top - bottom) / resolution;
            var lonStep = (right - left) / resolution;
            
            return ((int) ((longitude - left) / lonStep), (int) ((top - latitude) / latStep));
        }

        public static (double longitude, double latitude) FromLocalTileCoordinates(this (int x, int y, uint tileId) location, int zoom,
            int resolution)
        {
            FromLocalTileCoordinates(zoom, location.tileId, location.x, location.y, resolution, out var lon,
                out var lat);
            return (lon, lat);
        }

        public static void FromLocalTileCoordinates(int zoom, uint tileId, int x, int y, int resolution, out double longitude, out double latitude)
        {
            var tile = ToTile(zoom, tileId);
            
            var n = Math.PI - ((2.0 * Math.PI * tile.y) / Math.Pow(2.0, zoom));
            var left = ((tile.x / Math.Pow(2.0, zoom) * 360.0) - 180.0);
            var top = (180.0 / Math.PI * Math.Atan(Math.Sinh(n)));

            n = Math.PI - ((2.0 * Math.PI * (tile.y + 1)) / Math.Pow(2.0, zoom));
            var right = ((tile.x + 1) / Math.Pow(2.0, zoom) * 360.0) - 180.0;
            var bottom = (180.0 / Math.PI * Math.Atan(Math.Sinh(n)));
            
            var latStep = (top - bottom) / resolution;
            var lonStep = (right - left) / resolution;

            longitude = left + (lonStep * x);
            latitude = top - (y * latStep);
        }

        public static uint WorldTileLocalId((double longitude, double latitude) coordinate, int zoom)
        {
            return WorldTileLocalId(coordinate.longitude, coordinate.latitude, zoom);
        }

        public static uint WorldTileLocalId(double longitude, double latitude, int zoom)
        {
            var tile = WorldToTile(longitude, latitude, zoom);
            return ToLocalId(tile, zoom);
        }
        
        public static (uint x, uint y) WorldToTile(double longitude, double latitude, int zoom)
        {
            var n = (int) Math.Floor(Math.Pow(2, zoom)); // replace by bit shifting?

            var rad = (latitude / 180d) * System.Math.PI;

            var x = (uint) ((longitude + 180.0f) / 360.0f * n);
            var y = (uint) (
                (1.0f - Math.Log(Math.Tan(rad) + 1.0f / Math.Cos(rad))
                 / Math.PI) / 2f * n);
            
            return (x, y);
        }

        public static IEnumerable<(uint x, uint y)> TilesFor(
            this ((double longitude, double latitude) topLeft, (double longitude, double latitude) bottomRight) box,
            int zoom)
        {
            var topLeft = TileStatic.WorldToTile(box.topLeft.longitude, box.topLeft.latitude, zoom);
            var bottomRight = TileStatic.WorldToTile(box.bottomRight.longitude, box.bottomRight.latitude, zoom);
            
            for (var x = topLeft.x; x <= bottomRight.x; x++)
            for (var y = topLeft.y; y <= bottomRight.y; y++)
            {
                yield return (x, y);
            }
        }

        public static (uint x, uint y) ParentTileFor(this (uint x, uint y, int zoom) tile, int zoom)
        {
            while (true)
            {
                (uint x, uint y) directParent = (tile.x / 2, tile.y / 2);
                if (tile.zoom - zoom == 1)
                {
                    return directParent;
                }

                tile = (directParent.x, directParent.y, tile.zoom - 1);
            }
        }

        public static ((uint x, uint y) topLeft, (uint x, uint y) bottomRight) SubTileRangeFor(
            this (uint x, uint y, int zoom) tile, int zoom)
        {
            var factor = (uint)(1 << (zoom - tile.zoom));
            (uint x, uint y) topLeft = ((tile.x * factor), (tile.y * factor));
            return (topLeft, (topLeft.x + factor, topLeft.y + factor));
        }

        public static IEnumerable<(uint x, uint y)> SubTilesFor(this (uint x, uint y, int zoom) tile, int zoom)
        {
            var range = tile.SubTileRangeFor(zoom);
            for (var x = range.topLeft.x; x < range.bottomRight.x; x++)
            for (var y = range.topLeft.y; y < range.bottomRight.y; y++)
            {
                yield return (x, y);
            }
        }

        public static IEnumerable<(uint x, uint y)> SubTilesFor(this (uint x, uint y, int zoom) tile)
        {
            var x = tile.x * 2;
            var y = tile.y * 2;

            yield return (x + 0, y + 0);
            yield return (x + 1, y + 0);
            yield return (x + 0, y + 1);
            yield return (x + 1, y + 1);
        }
        
        private static GeometryFactory? _factory;

        private static GeometryFactory Factory
        {
            get => _factory ??= new GeometryFactory(new PrecisionModel(), 4326);
            set => _factory = value;
        }
        
        public static Polygon ToPolygon(int zoom, uint tileId, int margin = 5)
        {
            var box = Box(zoom, tileId);
            var left = box.topLeft.longitude;
            var right = box.bottomRight.longitude;
            var bottom = box.bottomRight.latitude;
            var top = box.topLeft.latitude;
            
            var factor = margin / 100f;
            var xMar = System.Math.Abs((right - left) * factor);
            var yMar = System.Math.Abs((top - bottom) * factor);

            // Get the factory
            var factory = Factory;

            // Create and fill sequence
            var cs = factory.CoordinateSequenceFactory.Create(5, 2);
            cs.SetOrdinate(0, Ordinate.X, left - xMar);
            cs.SetOrdinate(0, Ordinate.Y, top + yMar);
            cs.SetOrdinate(1, Ordinate.X, right + xMar);
            cs.SetOrdinate(1, Ordinate.Y, top + yMar);
            cs.SetOrdinate(2, Ordinate.X, right + xMar);
            cs.SetOrdinate(2, Ordinate.Y, bottom - yMar);
            cs.SetOrdinate(3, Ordinate.X, left - xMar);
            cs.SetOrdinate(3, Ordinate.Y, bottom - yMar);
            cs.SetOrdinate(4, Ordinate.X, left - xMar);
            cs.SetOrdinate(4, Ordinate.Y, top + yMar);

            // create shell
            var shell = factory.CreateLinearRing(cs);

            // return polygon
            return factory.CreatePolygon(shell);
        }
    }
}