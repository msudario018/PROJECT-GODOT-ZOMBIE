# Generates the tiling PBR texture set for the visual pipeline.
# Run from the project root:  powershell -File tools/generate_pbr_textures.ps1
# Produces albedo / roughness / normal maps for asphalt, wood, metal, concrete
# and chain link. The per-pixel work runs in compiled C# because
# System.Drawing.SetPixel is far too slow from PowerShell.

$ErrorActionPreference = 'Stop'
$outDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets/textures'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

public static class PbrGen
{
    const int Size = 256;

    // Hash-based tileable value noise: the lattice wraps at `cells`, so the
    // resulting texture repeats seamlessly.
    static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            long n = x * 374761393L + y * 668265263L + seed * 1442695041L;
            n = (n ^ (n >> 13)) * 1274126177L;
            n = n ^ (n >> 16);
            return (n & 0x7fffffff) / (double)0x7fffffff;
        }
    }

    static double ValueNoise(double u, double v, int cells, int seed)
    {
        int cx = (int)Math.Floor(u * cells);
        int cy = (int)Math.Floor(v * cells);
        double fx = u * cells - cx;
        double fy = v * cells - cy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);

        int w = (int)Math.Max(1, cells);
        double h00 = Hash((cx + 0 + w) % w, (cy + 0 + w) % w, seed);
        double h10 = Hash((cx + 1 + w) % w, (cy + 0 + w) % w, seed);
        double h01 = Hash((cx + 0 + w) % w, (cy + 1 + w) % w, seed);
        double h11 = Hash((cx + 1 + w) % w, (cy + 1 + w) % w, seed);

        double top = h00 * (1 - fx) + h10 * fx;
        double bot = h01 * (1 - fx) + h11 * fx;
        return top * (1 - fy) + bot * fy;
    }

    static double Fbm(double u, double v, int seed, int stretch)
    {
        double value = 0.0, amp = 0.5;
        int cells = 4;
        for (int o = 0; o < 4; o++)
        {
            value += ValueNoise(u * stretch, v * stretch, cells, seed + o * 17) * amp;
            amp *= 0.5;
            cells *= 2;
        }
        return Math.Min(1.0, Math.Max(0.0, value));
    }

    static double[] HeightField(int seed, int stretch, double grain, int pebbles)
    {
        var h = new double[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            double u = x / (double)Size, v = y / (double)Size;
            double value = Fbm(u, v, seed, stretch);
            value = value * (1 - grain) + grain * Fbm(u, v, seed + 5, stretch * 6);
            if (pebbles > 0 && ValueNoise(u, v, 32, seed + 99) > 1 - pebbles / 100.0)
                value += 0.25;
            h[y * Size + x] = Math.Min(1.0, value);
        }
        return h;
    }

    // Crossed diagonal wires, for the chain-link alpha cutout.
    static double[] WireField()
    {
        var h = new double[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            double u = x / (double)Size, v = y / (double)Size;
            double fu = (u * 8) % 1, fv = (v * 8) % 1;
            double d1 = Math.Abs(fu - 0.5) + Math.Abs(fv - 0.5);
            double d2 = Math.Abs(((fu + 0.5) % 1) - 0.5) + Math.Abs(((fv + 0.5) % 1) - 0.5);
            h[y * Size + x] = Math.Min(1.0, (1.0 - Math.Min(d1, d2)) * 4.0);
        }
        return h;
    }

    static Bitmap Albedo(double[] h, double r, double g, double b, double contrast, bool cutout)
    {
        var bmp = new Bitmap(Size, Size);
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            double n = h[y * Size + x];
            double c = 0.5 + (n - 0.5) * contrast;
            int cr = (int)Math.Min(255, Math.Max(0, r * c * 255));
            int cg = (int)Math.Min(255, Math.Max(0, g * c * 255));
            int cb = (int)Math.Min(255, Math.Max(0, b * c * 255));
            // Chain link needs real transparency in the voids so the material can
            // use an alpha-scissor cutout instead of blending.
            int a = cutout ? (int)Math.Min(255, Math.Max(0, (n - 0.25) * 2.0 * 255)) : 255;
            bmp.SetPixel(x, y, Color.FromArgb(a, cr, cg, cb));
        }
        return bmp;
    }

    static Bitmap Roughness(double[] h, double baseRough, double range)
    {
        var bmp = new Bitmap(Size, Size);
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            double r = baseRough + (h[y * Size + x] - 0.5) * range;
            int v = (int)Math.Min(255, Math.Max(0, r) * 255);
            bmp.SetPixel(x, y, Color.FromArgb(255, v, v, v));
        }
        return bmp;
    }

    // Central-difference gradient -> tangent-space normal map.
    static Bitmap Normal(double[] h, double strength)
    {
        var bmp = new Bitmap(Size, Size);
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            int xm = (x - 1 + Size) % Size, xp = (x + 1) % Size;
            int ym = (y - 1 + Size) % Size, yp = (y + 1) % Size;

            double dx = (h[y * Size + xp] - h[y * Size + xm]) * strength;
            double dy = (h[yp * Size + x] - h[ym * Size + x]) * strength;
            double len = Math.Sqrt(dx * dx + dy * dy + 1);

            bmp.SetPixel(x, y, Color.FromArgb(255,
                (int)((dx / len * 0.5 + 0.5) * 255),
                (int)((dy / len * 0.5 + 0.5) * 255),
                (int)((1 / len * 0.5 + 0.5) * 255)));
        }
        return bmp;
    }

    static void WriteSet(string dir, string name, double[] h,
                        double r, double g, double b, double contrast,
                        double baseRough, double roughRange, double normalStrength,
                        bool cutout = false)
    {
        Albedo(h, r, g, b, contrast, cutout).Save(Path.Combine(dir, name + "_albedo.png"), ImageFormat.Png);
        Roughness(h, baseRough, roughRange).Save(Path.Combine(dir, name + "_roughness.png"), ImageFormat.Png);
        Normal(h, normalStrength).Save(Path.Combine(dir, name + "_normal.png"), ImageFormat.Png);
        Console.WriteLine("  " + name + " -> albedo / roughness / normal" + (cutout ? " (alpha cutout)" : ""));
    }

    public static void Generate(string dir)
    {
        // Asphalt: dark, fine grain, scattered pebbles.
        WriteSet(dir, "asphalt", HeightField(11, 3, 0.35, 8), 0.34, 0.35, 0.37, 0.9, 0.85, 0.25, 2.5);

        // Weathered wood: strongly directional grain.
        WriteSet(dir, "wood", HeightField(23, 2, 0.15, 0), 0.62, 0.45, 0.27, 1.4, 0.75, 0.30, 3.5);

        // Painted steel: mostly flat with fine tooling marks.
        WriteSet(dir, "metal", HeightField(37, 6, 0.20, 0), 0.52, 0.55, 0.58, 0.6, 0.45, 0.20, 2.0);

        // Poured concrete: mid roughness, blotchy.
        WriteSet(dir, "concrete", HeightField(51, 4, 0.25, 0), 0.70, 0.69, 0.66, 0.8, 0.80, 0.20, 2.0);

        // Chain link: bright wire, dark voids (alpha cutout material).
        WriteSet(dir, "chainlink", WireField(), 0.78, 0.80, 0.82, 0.5, 0.40, 0.25, 1.5, cutout: true);
    }
}
'@

Write-Host "Generating PBR textures into $outDir"
[PbrGen]::Generate($outDir)
Write-Host 'Done.'
