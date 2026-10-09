using System.Reflection;
using System.Runtime.Loader;

string inputPath = Path.GetFullPath(args[0]);
string gameAssembly = File.Exists(inputPath) ? inputPath : Path.Combine(inputPath, "Stardew Valley.dll");
string gameDir = Path.GetDirectoryName(gameAssembly)!;
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    string path = Path.Combine(gameDir, name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(gameAssembly);
var type = assembly.GetType("StardewValley.Network.Compress.LZ4NetCompression", true)!;
var compression = Activator.CreateInstance(type, nonPublic: true)!;
var compress = type.GetMethod("CompressAbove")!;
var decompress = type.GetMethod("DecompressBytes")!;
var tryStream = type.GetMethod("TryDecompressStream")!;
if (args.Length == 3)
{
    string fixtureDir = Path.GetFullPath(args[2]);
    if (args[1] == "emit")
    {
        Directory.CreateDirectory(fixtureDir);
        for (int index = 0; index < 2; index++)
        {
            byte[] data = new byte[65536];
            if (index == 0) Array.Fill(data, (byte)65);
            else new Random(24642).NextBytes(data);
            byte[] block = (byte[])compress.Invoke(compression, new object[] { data, 1024 })!;
            File.WriteAllBytes(Path.Combine(fixtureDir, $"{index}.raw"), data);
            File.WriteAllBytes(Path.Combine(fixtureDir, $"{index}.lz4"), block);
        }
    }
    else if (args[1] == "verify")
    {
        for (int index = 0; index < 2; index++)
        {
            byte[] data = File.ReadAllBytes(Path.Combine(fixtureDir, $"{index}.raw"));
            byte[] block = File.ReadAllBytes(Path.Combine(fixtureDir, $"{index}.lz4"));
            byte[] decoded = (byte[])decompress.Invoke(compression, new object[] { block })!;
            if (!data.SequenceEqual(decoded)) throw new Exception("Cross-device fixture mismatch.");
            using var stream = new MemoryStream(block);
            object?[] streamArgs = { stream, null };
            if (!(bool)tryStream.Invoke(compression, streamArgs)! ||
                !data.SequenceEqual((byte[])streamArgs[1]!))
                throw new Exception("Cross-device network stream mismatch.");
        }
    }
    else throw new ArgumentException("Expected emit or verify.");
    Console.WriteLine($"PASS: {args[1]}, two cross-device game-format fixtures.");
}
var random = new Random(24642);
int checks = 0;
foreach (int size in new[] { 0, 1, 255, 1023, 1024, 1025, 65536, 262144 })
foreach (bool repetitive in new[] { false, true })
{
    byte[] input = new byte[size];
    if (repetitive) Array.Fill(input, (byte)65);
    else random.NextBytes(input);
    byte[] encoded = (byte[])compress.Invoke(compression, new object[] { input, 1024 })!;
    if (size < 1024)
    {
        if (!ReferenceEquals(input, encoded)) throw new Exception("Small packet changed.");
        continue;
    }
    if (encoded[0] != 127 || BitConverter.ToInt32(encoded, 5) != size)
        throw new Exception("Game compression header mismatch.");
    byte[] decoded = (byte[])decompress.Invoke(compression, new object[] { encoded })!;
    if (!input.SequenceEqual(decoded)) throw new Exception("Game byte roundtrip failed.");
    using var stream = new MemoryStream(encoded);
    object?[] streamArgs = { stream, null };
    if (!(bool)tryStream.Invoke(compression, streamArgs)! ||
        !input.SequenceEqual((byte[])streamArgs[1]!))
        throw new Exception("Game network-stream roundtrip failed.");
    checks++;
}
using (var smallStream = new MemoryStream(new byte[] { 1, 2, 3 }))
{
    object?[] streamArgs = { smallStream, null };
    if ((bool)tryStream.Invoke(compression, streamArgs)! || smallStream.Position != 0)
        throw new Exception("Uncompressed packet stream changed.");
}
Console.WriteLine($"PASS: exact game assembly, {checks} compressed byte/stream roundtrips, " +
    "compression threshold, 9-byte network header and uncompressed stream.");
