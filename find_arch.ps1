Add-Type -AssemblyName System.IO.Compression

$bytes = [System.IO.File]::ReadAllBytes("ROSTER-Official.bin")

# FBCHUNKS: 74-byte header, followed by zlib data
$zlibSize = [BitConverter]::ToUInt32($bytes, 10)
Write-Host "Zlib compressed size: $zlibSize"

# Create a proper byte array for the compressed data
[byte[]] $compressed = New-Object byte[] $zlibSize
[System.Array]::Copy($bytes, 74, $compressed, 0, $zlibSize)

# Decompress
$msIn = New-Object System.IO.MemoryStream($compressed, $false)
$msOut = New-Object System.IO.MemoryStream
$zstream = New-Object System.IO.Compression.DeflateStream($msIn, [System.IO.Compression.CompressionMode]::Decompress)
$zstream.CopyTo($msOut)
$zstream.Close()
$decompressed = $msOut.ToArray()
$msIn.Close()
$msOut.Close()

Write-Host "Decompressed size: $($decompressed.Length)"

# Skip 23-byte container header
$container = [byte[]]::new($decompressed.Length - 23)
[System.Array]::Copy($decompressed, 23, $container, 0, $container.Length)

Write-Host "Container size: $($container.Length)"

# Find Manning
$manningIdx = -1
for ($i = 0; $i -lt $container.Length - 7; $i++) {
    if ($container[$i] -eq 0x4D -and $container[$i+1] -eq 0x61 -and $container[$i+2] -eq 0x6E -and $container[$i+3] -eq 0x6E -and $container[$i+4] -eq 0x69 -and $container[$i+5] -eq 0x6E -and $container[$i+6] -eq 0x67) {
        $manningIdx = $i
        break
    }
}

if ($manningIdx -eq -1) {
    Write-Host "Manning NOT FOUND"
    exit
}

Write-Host "Manning found at offset: $manningIdx"

# Also search for "Arch" nearby
$archIdx = -1
for ($i = [Math]::Max(0, $manningIdx - 100); $i -lt [Math]::Min($container.Length, $manningIdx + 100); $i++) {
    if ($container[$i] -eq 0x41 -and $container[$i+1] -eq 0x72 -and $container[$i+2] -eq 0x63 -and $container[$i+3] -eq 0x68) {
        $archIdx = $i
        break
    }
}

if ($archIdx -ne -1) {
    Write-Host "Arch found at offset: $archIdx"
    $contextStart = [Math]::Max(0, $archIdx - 100)
    $contextEnd = [Math]::Min($container.Length, $archIdx + 200)
} else {
    $contextStart = [Math]::Max(0, $manningIdx - 50)
    $contextEnd = [Math]::Min($container.Length, $manningIdx + 300)
}

Write-Host "Dumping from $contextStart to $contextEnd"
Write-Host ""

$chunk = [byte[]]::new($contextEnd - $contextStart)
[System.Array]::Copy($container, $contextStart, $chunk, 0, $chunk.Length)

for ($i = 0; $i -lt $chunk.Length; $i += 16) {
    $lineLen = [Math]::Min(16, $chunk.Length - $i)
    $lineHex = ""
    $lineAscii = ""
    for ($j = 0; $j -lt $lineLen; $j++) {
        $b = $chunk[$i + $j]
        $lineHex += " $($b.ToString('X2'))"
        if ($b -ge 32 -and $b -lt 127) {
            $lineAscii += [char]$b
        } else {
            $lineAscii += "."
        }
    }
    Write-Host "$($contextStart + $i):$lineHex $lineAscii"
}
