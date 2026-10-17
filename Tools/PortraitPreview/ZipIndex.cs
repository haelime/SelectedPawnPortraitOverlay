using System.IO.Compression;
using System.Text;
using ZstdSharp;

namespace PortraitPreview;

// Minimal zip reader. RimWorld 1.7 stores textures with Zstandard (method 93), which ZipArchive cannot open.
public sealed class ZipIndex : IDisposable
{
    private const int EndOfCentralDirectorySignature = 0x06054b50;
    private const int CentralDirectorySignature = 0x02014b50;
    private const int LocalHeaderSize = 30;

    private readonly FileStream stream;

    public ZipIndex(string path)
    {
        stream = File.OpenRead(path);
        Entries = ReadCentralDirectory();
    }

    public IReadOnlyList<Entry> Entries { get; }

    public sealed record Entry(ZipIndex Archive, string Name, int Method, long CompressedSize, long Size, long LocalHeaderOffset);

    public byte[] Read(Entry entry)
    {
        var header = new byte[LocalHeaderSize];
        stream.Position = entry.LocalHeaderOffset;
        stream.ReadExactly(header);
        var nameLength = BitConverter.ToUInt16(header, 26);
        var extraLength = BitConverter.ToUInt16(header, 28);
        stream.Position = entry.LocalHeaderOffset + LocalHeaderSize + nameLength + extraLength;
        var compressed = new byte[entry.CompressedSize];
        stream.ReadExactly(compressed);

        switch (entry.Method)
        {
            case 0:
                return compressed;
            case 8:
                using (var deflate = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress))
                using (var output = new MemoryStream((int)entry.Size))
                {
                    deflate.CopyTo(output);
                    return output.ToArray();
                }
            case 93:
                using (var zstd = new Decompressor())
                {
                    return zstd.Unwrap(compressed).ToArray();
                }
            default:
                throw new NotSupportedException($"Zip compression method {entry.Method} in {entry.Name}");
        }
    }

    private List<Entry> ReadCentralDirectory()
    {
        // The end-of-central-directory record sits in the last 64 KiB + 22 bytes (it may carry a comment).
        var tailLength = (int)Math.Min(stream.Length, 65557);
        var tail = new byte[tailLength];
        stream.Position = stream.Length - tailLength;
        stream.ReadExactly(tail);
        var eocd = -1;
        for (var i = tailLength - 22; i >= 0; i--)
        {
            if (BitConverter.ToInt32(tail, i) == EndOfCentralDirectorySignature)
            {
                eocd = i;
                break;
            }
        }

        if (eocd < 0)
        {
            throw new InvalidDataException("Not a zip file");
        }

        var count = BitConverter.ToUInt16(tail, eocd + 10);
        var directorySize = BitConverter.ToUInt32(tail, eocd + 12);
        var directoryOffset = BitConverter.ToUInt32(tail, eocd + 16);
        var directory = new byte[directorySize];
        stream.Position = directoryOffset;
        stream.ReadExactly(directory);

        var entries = new List<Entry>(count);
        var position = 0;
        while (position + 46 <= directory.Length && BitConverter.ToInt32(directory, position) == CentralDirectorySignature)
        {
            var method = BitConverter.ToUInt16(directory, position + 10);
            var compressedSize = BitConverter.ToUInt32(directory, position + 20);
            var size = BitConverter.ToUInt32(directory, position + 24);
            var nameLength = BitConverter.ToUInt16(directory, position + 28);
            var extraLength = BitConverter.ToUInt16(directory, position + 30);
            var commentLength = BitConverter.ToUInt16(directory, position + 32);
            var localOffset = BitConverter.ToUInt32(directory, position + 42);
            var name = Encoding.UTF8.GetString(directory, position + 46, nameLength);
            entries.Add(new Entry(this, name, method, compressedSize, size, localOffset));
            position += 46 + nameLength + extraLength + commentLength;
        }

        return entries;
    }

    public void Dispose()
    {
        stream.Dispose();
    }
}
