using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClearOfficeViewer.UI.Interop;

/// <summary>
/// Generates a lightweight, deterministic 128x128 tiled monochrome Acrylic grain brush
/// to prevent color banding and add authentic frosted glass tactility with zero runtime CPU cost.
/// </summary>
public static class GlassNoiseBrush
{
    private static readonly Lazy<ImageBrush> CachedBrush = new(CreateFrozenNoiseBrush);

    public static ImageBrush Instance => CachedBrush.Value;

    private static ImageBrush CreateFrozenNoiseBrush()
    {
        const int size = 128;
        byte[] pixels = new byte[size * size * 4];

        // Deterministic xorshift32 PRNG for reproducible monochrome micro-grain
        uint state = 0x9E3779B9u;
        for (int i = 0; i < size * size; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;

            byte v = (byte)(state & 0xFF);
            // Subtle alpha modulation per pixel (mostly 0..22) so grain never looks harsh
            byte a = (byte)(v % 22);
            byte lum = (v & 1) == 0 ? (byte)255 : (byte)215;

            int idx = i * 4;
            pixels[idx + 0] = lum;
            pixels[idx + 1] = lum;
            pixels[idx + 2] = lum;
            pixels[idx + 3] = a;
        }

        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bmp.Freeze();

        var brush = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
            Opacity = 0.55
        };
        brush.Freeze();
        return brush;
    }
}
