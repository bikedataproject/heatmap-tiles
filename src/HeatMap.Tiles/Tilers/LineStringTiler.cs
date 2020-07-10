using System;
using System.Collections.Generic;
using HeatMap.Tiles.Tiles;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;

namespace HeatMap.Tiles.Tilers
{
    internal static class LineStringTiler
    {
        /// <summary>
        /// Adds the given linestring.
        /// </summary>
        /// <param name="heatMapTile">The tile.</param>
        /// <param name="lineString">The linestring.</param>
        /// <param name="cost">The cost.</param>
        public static void Add(this HeatMapTile heatMapTile, LineString lineString, uint cost = 1)
        {
            void Draw(int x, int y)
            {
                if (x < 0) return;
                if (y < 0) return;
                if (x >= heatMapTile.Resolution) return;
                if (y >= heatMapTile.Resolution) return;

                heatMapTile[x, y] += cost;
            }
            
            var tile = new Tile(heatMapTile.TileId);
            var tgt = new TileGeometryTransform(tile, heatMapTile.Resolution);
            int currentX = 0, currentY = 0;
            for (var c = 0; c < lineString.Coordinates.Length; c++)
            {
                var previousX = currentX;
                var previousY = currentY;
                tgt.Transform(lineString.CoordinateSequence,
                    c, ref currentX, ref currentY);
                if (c == 0) continue;
                
                Bresenhams(previousX, previousY, currentX, currentY, Draw);
            }
        }
        
        // https://stackoverflow.com/questions/11678693/all-cases-covered-bresenhams-line-algorithm
        internal static void Bresenhams(int x,int y,int x2, int y2,
            Action<int, int> draw) {
            var w = x2 - x ;
            var h = y2 - y ;
            int dx1 = 0, dy1 = 0, dx2 = 0, dy2 = 0 ;
            if (w<0) dx1 = -1 ; else if (w>0) dx1 = 1 ;
            if (h<0) dy1 = -1 ; else if (h>0) dy1 = 1 ;
            if (w<0) dx2 = -1 ; else if (w>0) dx2 = 1 ;
            var longest = Math.Abs(w) ;
            var shortest = Math.Abs(h) ;
            if (!(longest>shortest)) {
                longest = Math.Abs(h) ;
                shortest = Math.Abs(w) ;
                if (h<0) dy2 = -1 ; else if (h>0) dy2 = 1 ;
                dx2 = 0 ;            
            }
            var numerator = longest >> 1 ;
            for (var i=0;i<=longest;i++) {
                draw(x,y);
                numerator += shortest ;
                if (!(numerator<longest)) {
                    numerator -= longest ;
                    x += dx1 ;
                    y += dy1 ;
                } else {
                    x += dx2 ;
                    y += dy2 ;
                }
            }
        }

        /// <summary>
        /// Returns all the tiles this linestring is part of.
        /// </summary>
        /// <param name="lineString">The linestring.</param>
        /// <param name="zoom">The zoom.</param>
        /// <returns>An enumerable of all tiles.</returns>
        /// <remarks>It's possible this returns too many tiles, it's up to the 'Cut' method to exactly decide what tiles a linestring belongs in.</remarks>
        public static IEnumerable<ulong> Tiles(this LineString lineString, int zoom)
        {
            // always return the tile of the first coordinate.
            var previousTileId = Tile.CreateAroundLocationId(lineString.Coordinates[0].Y, lineString.Coordinates[0].X, zoom);
            yield return previousTileId;
            
            // return all the next tiles.
            HashSet<ulong> tiles = null;
            for (var c = 1; c < lineString.Coordinates.Length; c++)
            {
                var coordinate = lineString.Coordinates[c];
                var tileId = Tile.CreateAroundLocationId(coordinate.Y, coordinate.X, zoom);
                
                // only return changed ids.
                if (tileId == previousTileId) continue;
                
                // always two tiles or more, create hashset.
                // make sure to return only unique tiles.
                if (tiles == null) tiles = new HashSet<ulong> {previousTileId};

                // if the tiles are not neighbours then also return everything in between.
                if (!Tile.IsDirectNeighbour(tileId, previousTileId))
                { 
                    // determine all tiles between the two.
                    var previousCoordinate = lineString.Coordinates[c - 1];
                    var previousTile = new Tile(previousTileId);
                    var previousTileCoordinates =
                        previousTile.SubCoordinates(previousCoordinate.Y, previousCoordinate.X);
                    var nextTile = new Tile(tileId);
                    var nextTileCoordinates = nextTile.SubCoordinates(coordinate.Y, coordinate.X);

                    foreach (var (x, y) in Shared.LineBetween(previousTileCoordinates.x, previousTileCoordinates.y,
                        nextTileCoordinates.x, nextTileCoordinates.y))
                    {
                        var betweenTileId = Tile.CalculateTileId(zoom, x, y);
                        if (tiles.Contains(betweenTileId)) continue;
                        tiles.Add(betweenTileId);
                        yield return betweenTileId;
                    }
                }
                else
                {
                    if (tiles.Contains(tileId)) continue;
                    tiles.Add(tileId);
                    yield return tileId;
                }
                
                previousTileId = tileId; 
            }
        }
               
        /// <summary>
        /// Cuts the given linestring in one of more segments.
        /// </summary>
        /// <param name="tilePolygon">The tile polygon.</param>
        /// <param name="lineString">The linestring.</param>
        /// <returns>One or more segments.</returns>
        public static IEnumerable<LineString> Cut(this Polygon tilePolygon, LineString lineString)
        {
            var op = new OverlayOp(lineString, tilePolygon);
            var intersection = op.GetResultGeometry(SpatialFunction.Intersection);
            if (intersection.IsEmpty)
            {
                yield break;
            }

            switch (intersection)
            {
                case LineString ls:
                    // intersection is a linestring.
                    yield return ls;
                    yield break;
                
                case GeometryCollection gc:
                {
                    foreach (var geometry in gc.Geometries)
                    {
                        switch (geometry)
                        {
                            case LineString ls0:
                                yield return ls0;
                                break;
                            case Point _:
                                // The linestring only has a single point in this tile
                                // We skip it
                                // TODO check if this is correct
                                continue;
                            default:
                                throw new Exception(
                                    $"{nameof(LineStringTiler)}.{nameof(Cut)} failed: A geometry was found in the intersection that wasn't a {nameof(LineString)}.");
                        }
                    }

                    yield break;
                }

                default:
                    throw new Exception($"{nameof(LineStringTiler)}.{nameof(Cut)} failed: Unknown result.");
            }
        } 
    }
}