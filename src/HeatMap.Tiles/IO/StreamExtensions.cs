using System.IO;

namespace HeatMap.Tiles.IO
{
    internal static class StreamExtensions
    {
        public static void CopyBlockTo(this Stream stream, Stream target, int bytes, byte[]? buffer = null)
        {
            buffer ??= new byte[32384];

            while (bytes > buffer.Length)
            {
                stream.Read(buffer, 0, buffer.Length);
                bytes -= buffer.Length;
                target.Write(buffer, 0, buffer.Length);
            }
            
            stream.Read(buffer, 0, bytes);
            target.Write(buffer, 0, bytes);
        }
    }
}