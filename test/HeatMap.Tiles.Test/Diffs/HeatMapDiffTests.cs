using HeatMap.Tiles.Diffs;
using Xunit;

namespace HeatMap.Tiles.Test.Diffs
{
    public class HeatMapDiffTests
    {
        [Fact]
        public void HeatMapDiff_Create_ShouldBeEmpty()
        {
            var diff = new HeatMapDiff(12, 1024);
            
            Assert.Empty(diff);
        }
        
        [Fact]
        public void HeatMapDiff_Create_ShouldSetResolutionAndZoom()
        {
            var diff = new HeatMapDiff(12, 1024);
            
            Assert.Equal(12, diff.Zoom);
            Assert.Equal(1024U, diff.Resolution);
        }

        [Fact]
        public void HeatMapDiff_Set_Should_GetSame()
        {
            var diff = new HeatMapDiff(12, 1024);

            diff[4546541, 47744] = 46541;
            
            Assert.Equal(46541U,diff[4546541, 47744]);
        }
    }
}