using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

var factor = 4;
if (args.Length > 0 && args[^1].StartsWith("--factor="))
{
    if (!int.TryParse(args[^1][9..], out factor) || factor is not (1 or 2 or 4 or 8)) return 2;
    args = args[..^1];
}

if (args.Length == 2 && args[0] == "--batch")
{
    var resizedCount = 0;
    foreach (var line in File.ReadLines(args[1]))
    {
        var paths = JsonSerializer.Deserialize<string[]>(line);
        if (paths is not { Length: 2 }) return 3;
        var result = Resize(paths[0], paths[1], factor);
        if (result < 0) return 3;
        resizedCount += result;
    }
    Console.WriteLine("resized=" + resizedCount);
    return 0;
}

if (args.Length == 7 && args[0] == "--tile")
    return Resize(args[1], args[2], factor, int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]), int.Parse(args[6])) < 0 ? 3 : 0;
if (args.Length != 2) return 2;
return Resize(args[0], args[1], factor) < 0 ? 3 : 0;

static int Resize(string source, string target, int factor, int cropX = 0, int cropY = 0, int cropWidth = 0, int cropHeight = 0)
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var input = File.OpenRead(source);
        Span<byte> signature = stackalloc byte[8];
        ReadExactly(input, signature);
        if (!signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return -1;
        var chunkLength = ReadUInt32(input);
        Span<byte> chunkType = stackalloc byte[4];
        ReadExactly(input, chunkType);
        if (chunkLength != 13 || !ChunkTypeEquals(chunkType, "IHDR")) return -1;
        Span<byte> header = stackalloc byte[13];
        ReadExactly(input, header);
        var sourceWidth = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header[..4]));
        var sourceHeight = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4)));
        var bitDepth = header[8];
        var colorType = header[9];
        if (sourceWidth < 1 || sourceHeight < 1) return -1;
        bool tile = cropWidth > 0 && cropHeight > 0;
        if (!tile && sourceWidth < 512 && sourceHeight < 512)
        {
            File.Copy(source, target, true);
            return 0;
        }
        if (bitDepth != 8 || colorType is not (2 or 6) || header[10] != 0 || header[11] != 0 || header[12] != 0)
        {
            throw new InvalidDataException("large PNG must be 8-bit RGB/RGBA and non-interlaced");
        }
        if (!tile) { cropWidth = sourceWidth; cropHeight = sourceHeight; }
        if (cropX < 0 || cropY < 0 || cropX + cropWidth > sourceWidth || cropY + cropHeight > sourceHeight)
            throw new InvalidDataException("portrait tile exceeds source atlas");

        input.Position += 4; // IHDR CRC.
        uint firstIdatLength;
        Span<byte> idatType = stackalloc byte[4];
        while (true)
        {
            var length = ReadUInt32(input);
            ReadExactly(input, idatType);
            if (ChunkTypeEquals(idatType, "IDAT"))
            {
                firstIdatLength = length;
                break;
            }
            input.Position += checked((long)length + 4);
        }

        // Decode each PNG scanline and reduce it immediately. Peak pixel
        // storage is a handful of source rows and a small output buffer.
        var channels = colorType == 6 ? 4 : 3;
        var stride = checked(sourceWidth * channels);
        var width = Math.Max(1, 1 + (cropWidth - 1) / factor);
        var height = Math.Max(1, 1 + (cropHeight - 1) / factor);
        using var output = File.Create(target);
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var outputHeader = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(outputHeader.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(outputHeader.AsSpan(4, 4), (uint)height);
        outputHeader[8] = 8;
        outputHeader[9] = 6;
        PngOutput.WriteChunk(output, "IHDR", outputHeader);
        var previous = new byte[stride];
        var current = new byte[stride];
        var blockRows = new byte[factor][];
        for (int row = 0; row < factor; row++) blockRows[row] = new byte[stride];
        var outputRow = new byte[width * 4];
        var filteredOutputRow = new byte[outputRow.Length + 1];
        var idatOutput = new PngIdatOutputStream(output);
        ulong pixelHash = 14695981039346656037UL;
        using (var idat = new PngIdatStream(input, firstIdatLength))
        using (var decoded = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true))
        using (var encoded = new ZLibStream(idatOutput, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < cropY + cropHeight; y++)
            {
                var filter = decoded.ReadByte();
                if (filter < 0) return -1;
                ReadExactly(decoded, current.AsSpan());
                Unfilter(current, previous, channels, filter);
                int relativeY = y - cropY;
                if (relativeY < 0) { (previous, current) = (current, previous); continue; }
                Array.Copy(current, blockRows[relativeY % factor], stride);
                if ((relativeY % factor) == factor - 1 || relativeY == cropHeight - 1)
                {
                    var rowsInBlock = (relativeY % factor) + 1;
                    for (var x = 0; x < width; x++)
                    {
                        var outputOffset = x * 4;
                        var alphaSum = 0;
                        var redSum = 0;
                        var greenSum = 0;
                        var blueSum = 0;
                        for (var dy = 0; dy < factor; dy++)
                        {
                            var row = blockRows[Math.Min(dy, rowsInBlock - 1)];
                            for (var dx = 0; dx < factor; dx++)
                            {
                                var sourceX = cropX + Math.Min(cropWidth - 1, x * factor + dx);
                                var sourceOffset = sourceX * channels;
                                var alpha = channels == 4 ? row[sourceOffset + 3] : 255;
                                alphaSum += alpha;
                                redSum += row[sourceOffset] * alpha;
                                greenSum += row[sourceOffset + 1] * alpha;
                                blueSum += row[sourceOffset + 2] * alpha;
                            }
                        }
                        if (alphaSum == 0)
                        {
                            outputRow[outputOffset] = 0;
                            outputRow[outputOffset + 1] = 0;
                            outputRow[outputOffset + 2] = 0;
                            outputRow[outputOffset + 3] = 0;
                        }
                        else
                        {
                            outputRow[outputOffset] = (byte)((redSum + alphaSum / 2) / alphaSum);
                            outputRow[outputOffset + 1] = (byte)((greenSum + alphaSum / 2) / alphaSum);
                            outputRow[outputOffset + 2] = (byte)((blueSum + alphaSum / 2) / alphaSum);
                            outputRow[outputOffset + 3] = (byte)((alphaSum + factor * factor / 2) / (factor * factor));
                        }
                    }
                    for (int pixel = 0; pixel < outputRow.Length; pixel += 4)
                    {
                        byte alpha = outputRow[pixel + 3];
                        for (int channel = 0; channel < 4; channel++)
                            pixelHash = unchecked((pixelHash ^ (channel == 3 ? alpha : alpha == 255 ? outputRow[pixel + channel] : (byte)0)) * 1099511628211UL);
                    }
                    Buffer.BlockCopy(outputRow, 0, filteredOutputRow, 1, outputRow.Length);
                    encoded.Write(filteredOutputRow, 0, filteredOutputRow.Length);
                }
                (previous, current) = (current, previous);
            }
        }
        idatOutput.Finish();
        PngOutput.WriteChunk(output, "IEND", Array.Empty<byte>());
        if (!tile) File.WriteAllText(target + ".pixels.json", JsonSerializer.Serialize(new {
            hash = pixelHash.ToString("x16"), width, height, sourceWidth, sourceHeight
        }));
        return 1;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine(error.Message);
        return -1;
    }
}

static uint ReadUInt32(Stream stream)
{
    Span<byte> buffer = stackalloc byte[4];
    ReadExactly(stream, buffer);
    return BinaryPrimitives.ReadUInt32BigEndian(buffer);
}

static bool ChunkTypeEquals(ReadOnlySpan<byte> value, string expected) => value.Length == 4
    && value[0] == expected[0] && value[1] == expected[1]
    && value[2] == expected[2] && value[3] == expected[3];

static void ReadExactly(Stream stream, Span<byte> buffer)
{
    while (!buffer.IsEmpty)
    {
        var read = stream.Read(buffer);
        if (read <= 0) throw new EndOfStreamException();
        buffer = buffer[read..];
    }
}

static void Unfilter(byte[] row, byte[] previous, int channels, int filter)
{
    for (var i = 0; i < row.Length; i++)
    {
        var left = i >= channels ? row[i - channels] : 0;
        var up = previous[i];
        var upperLeft = i >= channels ? previous[i - channels] : 0;
        var predictor = filter switch
        {
            0 => 0,
            1 => left,
            2 => up,
            3 => (left + up) / 2,
            4 => Paeth(left, up, upperLeft),
            _ => throw new InvalidDataException("unsupported PNG filter " + filter)
        };
        row[i] = unchecked((byte)(row[i] + predictor));
    }
}

static int Paeth(int left, int up, int upperLeft)
{
    var estimate = left + up - upperLeft;
    var leftDistance = Math.Abs(estimate - left);
    var upDistance = Math.Abs(estimate - up);
    var upperLeftDistance = Math.Abs(estimate - upperLeft);
    return leftDistance <= upDistance && leftDistance <= upperLeftDistance ? left
        : upDistance <= upperLeftDistance ? up : upperLeft;
}

static class PngOutput
{
    private static readonly uint[] CrcTable = CreateCrcTable();

    internal static void WriteChunk(Stream output, string name, byte[] data)
    {
        var type = Encoding.ASCII.GetBytes(name);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        output.Write(type, 0, type.Length);
        if (data.Length > 0) output.Write(data, 0, data.Length);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, CalculateCrc(type, data));
        output.Write(crc);
    }

    internal static void WriteDataChunk(Stream output, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        Span<byte> type = stackalloc byte[4] { (byte)'I', (byte)'D', (byte)'A', (byte)'T' };
        output.Write(type);
        output.Write(data);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, CalculateCrc(type, data));
        output.Write(crc);
    }

    private static uint CalculateCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xffffffff;
        foreach (var value in type) crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xff];
        foreach (var value in data) crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xff];
        return ~crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xedb88320 ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }
}

sealed class PngIdatOutputStream : Stream
{
    private const int BufferSize = 32 * 1024;
    private readonly Stream output;
    private readonly byte[] buffer = new byte[BufferSize];
    private int count;
    private bool finished;

    internal PngIdatOutputStream(Stream output) => this.output = output;

    public override void Write(byte[] data, int offset, int length) => Write(data.AsSpan(offset, length));

    public override void Write(ReadOnlySpan<byte> data)
    {
        if (finished) throw new ObjectDisposedException(nameof(PngIdatOutputStream));
        while (!data.IsEmpty)
        {
            var copied = Math.Min(BufferSize - count, data.Length);
            data[..copied].CopyTo(buffer.AsSpan(count));
            count += copied;
            data = data[copied..];
            if (count == BufferSize) FlushChunk();
        }
    }

    private void FlushChunk()
    {
        if (count == 0) return;
        PngOutput.WriteDataChunk(output, buffer.AsSpan(0, count));
        count = 0;
    }

    internal void Finish()
    {
        if (finished) return;
        FlushChunk();
        finished = true;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !finished;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => FlushChunk();
    public override int Read(byte[] data, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

sealed class PngIdatStream : Stream
{
    private readonly Stream source;
    private uint remaining;
    private bool ended;
    private readonly byte[] header = new byte[8];
    private readonly byte[] crc = new byte[4];

    internal PngIdatStream(Stream source, uint firstLength)
    {
        this.source = source;
        remaining = firstLength;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count == 0 || ended) return 0;
        while (remaining == 0)
        {
            ReadFully(source, crc);
            ReadFully(source, header);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
            if (header[4] != 'I' || header[5] != 'D' || header[6] != 'A' || header[7] != 'T')
            {
                ended = true;
                return 0;
            }
            remaining = length;
        }
        var requested = (int)Math.Min((uint)count, remaining);
        var read = source.Read(buffer, offset, requested);
        if (read <= 0) throw new EndOfStreamException();
        remaining -= (uint)read;
        return read;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private static void ReadFully(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
