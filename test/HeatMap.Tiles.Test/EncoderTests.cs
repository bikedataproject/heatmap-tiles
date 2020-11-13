using Xunit;

namespace HeatMap.Tiles.Test
{
    public class EncoderTests
    {
        [Fact]
        public void Encoder_Encode_0_0_ShouldReturn_0()
        {
            Assert.Equal(0UL, Encoder.Encode(0, 0));
        }

        [Fact]
        public void Encoder_Encode_0_100_ShouldReturn_100()
        {
            Assert.Equal(100UL, Encoder.Encode(0, 100));
        }

        [Fact]
        public void Encoder_Encode_100_0_ShouldReturn_429496729600()
        {
            Assert.Equal(429496729600UL, Encoder.Encode(100, 0));
        }

        [Fact]
        public void Encode_Decode_0_ShouldReturn_0_0()
        {
            var (userCount, tripCount) = Encoder.Decode(0);
            Assert.Equal(0U, userCount);
            Assert.Equal(0U, tripCount);
        }

        [Fact]
        public void Encode_Decode_100_ShouldReturn_0_100()
        {
            var (userCount, tripCount) = Encoder.Decode(100);
            Assert.Equal(0U, userCount);
            Assert.Equal(100U, tripCount);
        }

        [Fact]
        public void Encode_Decode_4294967296000_ShouldReturn_100_0()
        {
            var (userCount, tripCount) = Encoder.Decode(429496729600UL);
            Assert.Equal(100U, userCount);
            Assert.Equal(0U, tripCount);
        }
    }
}