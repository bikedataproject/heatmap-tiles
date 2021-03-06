using System.IO;
using Xunit;

namespace HeatMap.Tiles.Test
{
    public class HeatMapTileTests
    {
        [Fact]
        public void HeatMapTile_Create_ShouldBeEmpty()
        {
            var tile = new HeatMapTile<uint>(new MemoryStream(),1024);
            for (var x = 0; x < tile.Resolution; x++)
            for (var y = 0; y < tile.Resolution; y++)
            {
                Assert.Equal(0U, tile[x, y]);
            }
        }
        
        [Fact]
        public void HeatMapTile_Set_10_10_Should_Get10_10()
        {
            var tile = new HeatMapTile<uint>(new MemoryStream(),1024) {[10, 10] = 441141};

            Assert.Equal(441141U, tile[10, 10]);
        }

        [Fact]
        public void HeatMapTile_Set_10_10_Should_WriteToStream()
        {
            var stream = new MemoryStream();
            var tile = new HeatMapTile<uint>(stream,1024) {[10, 10] = 441141};

            stream.Seek(0, SeekOrigin.Begin);
            tile = new HeatMapTile<uint>(stream);

            Assert.Equal(1024U, tile.Resolution);
            Assert.Equal(441141U, tile[10, 10]);
        }
    }
}