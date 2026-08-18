using System;

var path = @"C:\Users\Ninja\Documents\Mods\CFB27 mods\ANTICHEAT\EAAntiCheat.GameServiceLauncher.exe";
var bytes = File.ReadAllBytes(path);
var search = System.Text.Encoding.ASCII.GetBytes("Madden26.exe");

for (int i = 0; i <= bytes.Length - search.Length; i++) {
    bool found = true;
    for (int j = 0; j < search.Length; j++) {
        if (bytes[i + j] != search[j]) { found = false; break; }
    }
    if (found) {
        Console.WriteLine($"Found 'Madden26.exe' at offset 0x{i:X} ({i})");
        Console.WriteLine($"Context: {BitConverter.ToString(bytes[Math.Max(0,i-4)..(i+20)])}");
    }
}

// Also find all ASCII string references longer than 4 chars
Console.WriteLine("\nAll exe/dll references:");
for (int i = 0; i < bytes.Length - 4; i++) {
    if (bytes[i] >= 0x20 && bytes[i] <= 0x7E) {
        int end = i;
        while (end < bytes.Length && bytes[end] >= 0x20 && bytes[end] <= 0x7E) end++;
        if (end - i >= 4) {
            var s = System.Text.Encoding.ASCII.GetString(bytes[i..end]);
            if (s.Contains(".exe") || s.Contains(".dll") || s.Contains(".DLL"))
                Console.WriteLine($"  0x{i:X}: {s}");
            i = end;
        }
    }
}
