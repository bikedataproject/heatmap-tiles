using System;

namespace HeatMap.Tiles.Draw
{
    internal static class Bresenhams
    {
        // https://stackoverflow.com/questions/11678693/all-cases-covered-bresenhams-line-algorithm
        internal static void Draw(long x,long y,long x2, long y2,
            Action<long, long> draw) {
            var w = x2 - x ;
            var h = y2 - y ;
            long dx1 = 0, dy1 = 0, dx2 = 0, dy2 = 0 ;
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
            for (var i=0L;i<=longest;i++) {
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
    }
}