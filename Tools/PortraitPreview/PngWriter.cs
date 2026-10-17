using System.Buffers.Binary;
using System.IO.Compression;

namespace PortraitPreview;

public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(string path, RgbaImage image)
    {
        File.WriteAllBytes(path, Encode(image));
    }

    public static byte[] Encode(RgbaImage image)
    {
        using var file = new MemoryStream();
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        WriteChunk(file, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            // Paeth-filter every row; flat panel backgrounds then compress to almost nothing.
            var stride = image.Width * 4;
            var filtered = new byte[stride];
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < stride; x++)
                {
                    var i = (y * stride) + x;
                    var left = x >= 4 ? image.Pixels[i - 4] : 0;
                    var up = y > 0 ? image.Pixels[i - stride] : 0;
                    var upLeft = x >= 4 && y > 0 ? image.Pixels[i - stride - 4] : 0;
                    filtered[x] = (byte)(image.Pixels[i] - Paeth(left, up, upLeft));
                }

                zlib.WriteByte(4);
                zlib.Write(filtered, 0, stride);
            }
        }

        WriteChunk(file, "IDAT", compressed.ToArray());
        WriteChunk(file, "IEND", Array.Empty<byte>());
        return file.ToArray();
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpLeft = Math.Abs(estimate - upLeft);
        if (toLeft <= toUp && toLeft <= toUpLeft)
        {
            return left;
        }

        return toUp <= toUpLeft ? up : upLeft;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = UpdateCrc(UpdateCrc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint UpdateCrc(uint crc, byte[] data)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
