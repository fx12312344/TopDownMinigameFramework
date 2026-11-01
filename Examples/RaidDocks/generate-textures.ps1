param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'Textures\Items'))
$ErrorActionPreference = 'Stop'
# Build-time only, no Unity dependencies, no image/AI libraries, no downloaded artwork.
if (-not ('RaidDocksBuild.Png' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
namespace RaidDocksBuild
{
    public static class Png
    {
        private static void U32(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value >> 24)); stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value);
        }
        private static void Chunk(Stream stream, string type, byte[] data)
        {
            U32(stream, (uint)data.Length);
            byte[] name = Encoding.ASCII.GetBytes(type);
            stream.Write(name, 0, name.Length); stream.Write(data, 0, data.Length);
            uint crc = 0xffffffffu;
            foreach (byte b in name) crc = Crc(crc, b);
            foreach (byte b in data) crc = Crc(crc, b);
            U32(stream, crc ^ 0xffffffffu);
        }
        private static uint Crc(uint crc, byte b)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
            return crc;
        }
        public static void Write(string path, string shape)
        {
            const int width = 64, height = 64;
            byte[] raw = new byte[(1 + width * 4) * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * (1 + width * 4) + 1 + x * 4;
                bool inside = true;
                byte value = 255;
                if (shape == "floor") value = (byte)(((x / 8 + y / 8) % 2 == 0) ? 235 : 195);
                if (shape == "wall") value = (byte)(x < 3 || y < 3 || x > 60 || y > 60 || y % 16 < 2 || (x + ((y / 16) % 2) * 16) % 32 < 2 ? 170 : 240);
                if (shape == "actor") { inside = (x - 31) * (x - 31) + (y - 31) * (y - 31) <= 26 * 26; value = (byte)(x > 28 && x < 35 && y < 24 ? 170 : 255); }
                if (shape == "crate") { inside = x >= 5 && x <= 58 && y >= 5 && y <= 58; value = (byte)(x < 10 || x > 53 || y < 10 || y > 53 || Math.Abs(x - y) < 3 || Math.Abs(x + y - 63) < 3 ? 180 : 255); }
                if (shape == "circuit") { inside = x >= 6 && x <= 57 && y >= 15 && y <= 48; value = (byte)((x % 10 < 3 && y > 20 && y < 44) || Math.Abs(y - 31) < 3 ? 180 : 255); }
                if (shape == "medical") { inside = x >= 14 && x <= 49 && y >= 5 && y <= 58; value = (byte)(((x >= 27 && x <= 36 && y >= 17 && y <= 46) || (x >= 20 && x <= 43 && y >= 27 && y <= 36)) ? 165 : 255); }
                if (shape == "carbine") { inside = (x >= 5 && x <= 58 && y >= 22 && y <= 30) || (x >= 14 && x <= 43 && y >= 29 && y <= 36) || (x >= 29 && x <= 35 && y >= 34 && y <= 49) || (x >= 5 && x <= 15 && y >= 29 && y <= 42); }
                raw[i] = raw[i + 1] = raw[i + 2] = value; raw[i + 3] = (byte)(inside ? 255 : 0);
            }
            // One valid uncompressed DEFLATE block, with a zlib wrapper + Adler32.
            using (var z = new MemoryStream())
            using (var png = new MemoryStream())
            {
                z.WriteByte(0x78); z.WriteByte(0x01); z.WriteByte(0x01);
                int len = raw.Length;
                z.WriteByte((byte)len); z.WriteByte((byte)(len >> 8));
                z.WriteByte((byte)~len); z.WriteByte((byte)(~len >> 8)); z.Write(raw, 0, raw.Length);
                uint a = 1, b = 0;
                foreach (byte v in raw) { a = (a + v) % 65521; b = (b + a) % 65521; }
                U32(z, (b << 16) | a);
                byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                png.Write(signature, 0, signature.Length);
                byte[] ihdr = { 0, 0, 0, 64, 0, 0, 0, 64, 8, 6, 0, 0, 0 };
                Chunk(png, "IHDR", ihdr); Chunk(png, "IDAT", z.ToArray()); Chunk(png, "IEND", new byte[0]);
                File.WriteAllBytes(path, png.ToArray());
            }
        }
    }
}
'@
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
foreach ($shape in @('floor', 'wall', 'actor', 'crate', 'circuit', 'medical', 'carbine')) {
    [RaidDocksBuild.Png]::Write((Join-Path $OutputDirectory "dock_$shape.png"), $shape)
}
Write-Output "Generated seven 64x64 geometric PNG test sprites: $OutputDirectory"
