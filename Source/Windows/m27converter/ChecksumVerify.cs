using System.IO.Compression;

uint CRCPOLY_BE = 0x04C11DB7u;

uint[] crcTableBe = new uint[16];
{
    uint crc = 0x80000000u;
    crcTableBe[0] = 0;
    for (int i = 1; i < 16; i <<= 1)
    {
        crc = (crc << 1) ^ ((crc & 0x80000000u) != 0 ? CRCPOLY_BE : 0);
        for (int j = 0; j < i; j++)
            crcTableBe[i + j] = crc ^ crcTableBe[j];
    }
}

uint Crc32Be(uint crc, byte[] p, int len, int start = 0)
{
    int x = start;
    crc ^= 0xFFFFFFFFu;
    while (len-- > 0)
    {
        crc ^= (uint)p[x++] << 24;
        crc = ((crc << 4) ^ crcTableBe[crc >> 28]);
        crc = ((crc << 4) ^ crcTableBe[crc >> 28]);
    }
    return crc ^ 0xFFFFFFFFu;
}

void Check(string path)
{
    var fb = File.ReadAllBytes(path);
    byte[] inner;
    using (var ms = new MemoryStream())
    using (var input = new MemoryStream(fb, 0x4A + 2, fb.Length - 0x4A - 2))
    using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
    { deflate.CopyTo(ms); inner = ms.ToArray(); }

    uint storedCrc = BitConverter.ToUInt32(fb, 0x1A);
    uint storedLen = BitConverter.ToUInt32(fb, 0x12);
    uint calcCrc = Crc32Be(0, inner, inner.Length);

    Console.WriteLine(path);
    Console.WriteLine($"  stored @0x1A CRC(LE32)  = 0x{storedCrc:X8}");
    Console.WriteLine($"  computed crc32_be      = 0x{calcCrc:X8}");
    Console.WriteLine($"  stored @0x12 len       = {storedLen}");
    Console.WriteLine($"  actual inner len       = {inner.Length}");
    Console.WriteLine($"  CRC  match = {storedCrc == calcCrc},  LEN  match = {storedLen == inner.Length}");
}

Check(@"C:\Users\Ninja\Documents\Madden NFL 26\saves\ROSTER-Official27TEST");
Check(@"C:\Users\Ninja\Documents\Madden NFL 27 Beta\Saves\ROSTER-MADDEN27");
Check(@"C:\Users\Ninja\AppData\Local\Temp\opencode\rtout\ROSTER-M27CONVERTED");
