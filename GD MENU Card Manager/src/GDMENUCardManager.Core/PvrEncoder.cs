using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GDMENUCardManager.Core
{
    public static class PvrEncoder
    {
        // BOX.DAT constants (256x256)
        public const int TextureWidth = 256;
        public const int TextureHeight = 256;
        public const int PixelDataSize = TextureWidth * TextureHeight * 2;   // 131,072 bytes
        public const int TotalPvrSize = TotalHeaderSize + PixelDataSize;     // 131,104 bytes

        // ICON.DAT constants (128x128)
        public const int IconWidth = 128;
        public const int IconHeight = 128;
        public const int IconPixelDataSize = IconWidth * IconHeight * 2;     // 32,768 bytes
        public const int TotalIconPvrSize = TotalHeaderSize + IconPixelDataSize;  // 32,800 bytes

        // Common constants
        public const uint GlobalIndex = 1001;
        public const int GbixHeaderSize = 16;  // GBIX(4) + size(4) + index(4) + pad(4)
        public const int PvrtHeaderSize = 16;  // PVRT(4) + size(4) + format(2) + pad(2) + w(2) + h(2)
        public const int TotalHeaderSize = GbixHeaderSize + PvrtHeaderSize;  // 32 bytes

        // PVRT format constants
        private const byte PixelFormatArgb1555 = 0x00;
        private const byte PixelFormatRgb565 = 0x01;
        private const byte PixelFormatArgb4444 = 0x02;
        private const byte DataFormatSquareTwiddled = 0x01;

        /// <summary>
        /// Resizes to 256x256 without preserving aspect ratio. Pixel format is chosen from the
        /// image's alpha channel.
        /// </summary>
        public static byte[] EncodeFromFile(string imagePath)
        {
            using var image = Image.Load<Bgra32>(imagePath);
            return EncodeFromImage(image, TextureWidth, TextureHeight);
        }

        /// <summary>
        /// Resizes to 128x128 without preserving aspect ratio. Pixel format is chosen from the
        /// image's alpha channel.
        /// </summary>
        public static byte[] EncodeIconFromFile(string imagePath)
        {
            using var image = Image.Load<Bgra32>(imagePath);
            return EncodeFromImage(image, IconWidth, IconHeight);
        }

        public static byte[] EncodeFromStream(Stream stream)
        {
            using var image = Image.Load<Bgra32>(stream);
            return EncodeFromImage(image, TextureWidth, TextureHeight);
        }

        /// <summary>
        /// Encode a stream to 128x128 PVR format for ICON.DAT (TWIDDLED, Global Index 1001).
        /// </summary>
        public static byte[] EncodeIconFromStream(Stream stream)
        {
            using var image = Image.Load<Bgra32>(stream);
            return EncodeFromImage(image, IconWidth, IconHeight);
        }

        /// <summary>
        /// For pixels already decoded (e.g., re-encoding an existing DAT entry).
        /// </summary>
        public static byte[] EncodeFromPixels(byte[] bgraPixels, int width, int height)
        {
            using var image = Image.LoadPixelData<Bgra32>(bgraPixels, width, height);
            return EncodeFromImage(image, TextureWidth, TextureHeight);
        }

        public static byte[] EncodeIconFromPixels(byte[] bgraPixels, int width, int height)
        {
            using var image = Image.LoadPixelData<Bgra32>(bgraPixels, width, height);
            return EncodeFromImage(image, IconWidth, IconHeight);
        }

        private static byte[] EncodeFromImage(Image<Bgra32> image, int targetWidth, int targetHeight)
        {
            // Forced resize. Aspect ratio is deliberately not preserved.
            image.Mutate(ctx => ctx.Resize(targetWidth, targetHeight));

            byte pixelFormat = ChoosePixelFormat(image, targetWidth, targetHeight);

            // Extract pixel data in linear order
            var pixelData = new ushort[targetWidth * targetHeight];
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < targetHeight; y++)
                {
                    var rowSpan = accessor.GetRowSpan(y);
                    for (int x = 0; x < targetWidth; x++)
                    {
                        var pixel = rowSpan[x];
                        pixelData[y * targetWidth + x] = pixelFormat switch
                        {
                            PixelFormatArgb1555 => Bgra32ToArgb1555(pixel),
                            PixelFormatArgb4444 => Bgra32ToArgb4444(pixel),
                            _ => Bgra32ToRgb565(pixel)
                        };
                    }
                }
            });

            // Twiddle the pixel data
            var twiddledData = TwiddleTexture(pixelData, targetWidth, targetHeight);

            // Build the PVR file
            return BuildPvrFile(twiddledData, targetWidth, targetHeight, pixelFormat);
        }

        /// <summary>
        /// Pick the PVR pixel format that best fits the image's alpha channel.
        /// Fully opaque images get RGB565, on/off alpha gets ARGB1555 and partial alpha gets ARGB4444.
        /// openMenu reads the format byte from each DAT entry so all three are safe to emit.
        /// </summary>
        private static byte ChoosePixelFormat(Image<Bgra32> image, int width, int height)
        {
            bool hasTransparency = false;
            bool hasPartialAlpha = false;

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var rowSpan = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        byte a = rowSpan[x].A;
                        if (a == 255)
                            continue;
                        hasTransparency = true;
                        if (a != 0)
                        {
                            hasPartialAlpha = true;
                            return;
                        }
                    }
                }
            });

            if (!hasTransparency)
                return PixelFormatRgb565;
            return hasPartialAlpha ? PixelFormatArgb4444 : PixelFormatArgb1555;
        }

        private static ushort Bgra32ToRgb565(Bgra32 pixel)
        {
            // RGB565: RRRR_RGGG_GGGB_BBBB
            int r = (pixel.R >> 3) & 0x1F;  // 5 bits
            int g = (pixel.G >> 2) & 0x3F;  // 6 bits
            int b = (pixel.B >> 3) & 0x1F;  // 5 bits
            return (ushort)((r << 11) | (g << 5) | b);
        }

        /// <summary>
        /// Convert BGRA32 to ARGB1555.
        /// </summary>
        private static ushort Bgra32ToArgb1555(Bgra32 pixel)
        {
            // ARGB1555: A_RRRRR_GGGGG_BBBBB
            int a = pixel.A >= 128 ? 1 : 0;  // 1 bit
            int r = (pixel.R >> 3) & 0x1F;   // 5 bits
            int g = (pixel.G >> 3) & 0x1F;   // 5 bits
            int b = (pixel.B >> 3) & 0x1F;   // 5 bits
            return (ushort)((a << 15) | (r << 10) | (g << 5) | b);
        }

        /// <summary>
        /// Convert BGRA32 to ARGB4444.
        /// </summary>
        private static ushort Bgra32ToArgb4444(Bgra32 pixel)
        {
            // ARGB4444: AAAA_RRRR_GGGG_BBBB
            int a = (pixel.A >> 4) & 0xF;  // 4 bits
            int r = (pixel.R >> 4) & 0xF;  // 4 bits
            int g = (pixel.G >> 4) & 0xF;  // 4 bits
            int b = (pixel.B >> 4) & 0xF;  // 4 bits
            return (ushort)((a << 12) | (r << 8) | (g << 4) | b);
        }

        private static ushort[] TwiddleTexture(ushort[] linearData, int width, int height)
        {
            var twiddled = new ushort[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int mortonIndex = GetMortonIndex(x, y);
                    twiddled[mortonIndex] = linearData[y * width + x];
                }
            }

            return twiddled;
        }

        private static int GetMortonIndex(int x, int y)
        {
            // Interleave bits: x in odd positions, y in even positions (Dreamcast PVR standard)
            return (int)(Part1By1(x) << 1 | Part1By1(y));
        }

        // Spreads a zero bit between each of the low 16 bits, so abcd becomes 0a0b0c0d.
        private static uint Part1By1(int n)
        {
            uint x = (uint)n;
            x = (x | (x << 8)) & 0x00FF00FF;
            x = (x | (x << 4)) & 0x0F0F0F0F;
            x = (x | (x << 2)) & 0x33333333;
            x = (x | (x << 1)) & 0x55555555;
            return x;
        }

        private static byte[] BuildPvrFile(ushort[] twiddledData, int width, int height, byte pixelFormat)
        {
            int pixelDataSize = width * height * 2;
            int totalSize = TotalHeaderSize + pixelDataSize;
            var pvr = new byte[totalSize];

            using var ms = new MemoryStream(pvr);
            using var writer = new BinaryWriter(ms);

            // GBIX header (16 bytes)
            writer.Write((byte)'G');
            writer.Write((byte)'B');
            writer.Write((byte)'I');
            writer.Write((byte)'X');
            writer.Write((uint)8);            // Size of data following (index + padding = 8)
            writer.Write((uint)GlobalIndex);  // Global Index value
            writer.Write((uint)0);            // Padding

            // PVRT header (16 bytes)
            writer.Write((byte)'P');
            writer.Write((byte)'V');
            writer.Write((byte)'R');
            writer.Write((byte)'T');
            writer.Write((uint)(pixelDataSize + 8));  // Data size (pixel data + 8 for format info)
            writer.Write(pixelFormat);                // Pixel format (1 byte)
            writer.Write(DataFormatSquareTwiddled);   // Data format (1 byte)
            writer.Write((ushort)0);                   // Padding
            writer.Write((ushort)width);               // Width
            writer.Write((ushort)height);              // Height

            // Pixel data
            foreach (var pixel in twiddledData)
            {
                writer.Write(pixel);
            }

            return pvr;
        }

        /// <summary>
        /// Handles only the square twiddled formats this encoder emits, at 256x256 or 128x128.
        /// Returns null for anything else.
        /// </summary>
        public static (byte[] pixels, int width, int height)? DecodePvr(byte[] pvrData)
        {
            if (pvrData == null || pvrData.Length < TotalHeaderSize)
                return null;

            using var ms = new MemoryStream(pvrData);
            using var reader = new BinaryReader(ms);

            if (reader.ReadByte() != 'G' ||
                reader.ReadByte() != 'B' ||
                reader.ReadByte() != 'I' ||
                reader.ReadByte() != 'X')
                return null;

            reader.ReadUInt32();  // GBIX size (skip)
            reader.ReadUInt32();  // Global index (skip)
            reader.ReadUInt32();  // Padding (skip)

            // Verify PVRT header
            if (reader.ReadByte() != 'P' ||
                reader.ReadByte() != 'V' ||
                reader.ReadByte() != 'R' ||
                reader.ReadByte() != 'T')
                return null;

            uint dataSize = reader.ReadUInt32();
            byte pixelFormat = reader.ReadByte();
            byte dataFormat = reader.ReadByte();
            reader.ReadUInt16();  // Padding
            ushort width = reader.ReadUInt16();
            ushort height = reader.ReadUInt16();

            bool isKnownPixelFormat = pixelFormat == PixelFormatRgb565 ||
                                      pixelFormat == PixelFormatArgb1555 ||
                                      pixelFormat == PixelFormatArgb4444;
            if (!isKnownPixelFormat || dataFormat != DataFormatSquareTwiddled)
                return null;

            // Validate supported dimensions (256x256 for BOX or 128x128 for ICON)
            bool isValidBox = (width == TextureWidth && height == TextureHeight);
            bool isValidIcon = (width == IconWidth && height == IconHeight);
            if (!isValidBox && !isValidIcon)
                return null;

            // Validate data length
            int expectedPixelDataSize = width * height * 2;
            int expectedTotalSize = TotalHeaderSize + expectedPixelDataSize;
            if (pvrData.Length < expectedTotalSize)
                return null;

            // Read twiddled pixel data
            var twiddledData = new ushort[width * height];
            for (int i = 0; i < twiddledData.Length; i++)
            {
                twiddledData[i] = reader.ReadUInt16();
            }

            // Untwiddle
            var linearData = UntwiddleTexture(twiddledData, width, height);

            // Convert to BGRA32
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < linearData.Length; i++)
            {
                var value = linearData[i];
                int a, r, g, b;

                switch (pixelFormat)
                {
                    case PixelFormatArgb1555:
                        a = (value & 0x8000) != 0 ? 255 : 0;
                        r = ((value >> 10) & 0x1F) << 3;
                        g = ((value >> 5) & 0x1F) << 3;
                        b = (value & 0x1F) << 3;

                        // Replicate the high bits into the low ones so white stays white.
                        r |= (r >> 5);
                        g |= (g >> 5);
                        b |= (b >> 5);
                        break;
                    case PixelFormatArgb4444:
                        a = ((value >> 12) & 0xF) * 17;
                        r = ((value >> 8) & 0xF) * 17;
                        g = ((value >> 4) & 0xF) * 17;
                        b = (value & 0xF) * 17;
                        break;
                    default:
                        a = 255;
                        r = ((value >> 11) & 0x1F) << 3;
                        g = ((value >> 5) & 0x3F) << 2;
                        b = (value & 0x1F) << 3;

                        // Replicate the high bits into the low ones so white stays white.
                        r |= (r >> 5);
                        g |= (g >> 6);
                        b |= (b >> 5);
                        break;
                }

                int offset = i * 4;
                pixels[offset + 0] = (byte)b;      // B
                pixels[offset + 1] = (byte)g;      // G
                pixels[offset + 2] = (byte)r;      // R
                pixels[offset + 3] = (byte)a;      // A
            }

            return (pixels, width, height);
        }

        private static ushort[] UntwiddleTexture(ushort[] twiddledData, int width, int height)
        {
            var linear = new ushort[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int mortonIndex = GetMortonIndex(x, y);
                    linear[y * width + x] = twiddledData[mortonIndex];
                }
            }

            return linear;
        }

        public static bool SavePvrAsPng(byte[] pvrData, string outputPath)
        {
            var decoded = DecodePvr(pvrData);
            if (decoded == null)
                return false;

            var (pixels, width, height) = decoded.Value;

            using var image = Image.LoadPixelData<Bgra32>(pixels, width, height);
            image.SaveAsPng(outputPath);
            return true;
        }

        public static byte[] ConvertPvrToPngBytes(byte[] pvrData)
        {
            var decoded = DecodePvr(pvrData);
            if (decoded == null)
                return null;

            var (pixels, width, height) = decoded.Value;

            using var image = Image.LoadPixelData<Bgra32>(pixels, width, height);
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Round-trips through decode and re-encode, so the icon's pixel format is re-chosen and
        /// may differ from the source entry's.
        /// </summary>
        public static byte[] DownscaleBoxPvrToIcon(byte[] boxPvrData)
        {
            var decoded = DecodePvr(boxPvrData);
            if (decoded == null)
                return null;

            var (pixels, width, height) = decoded.Value;

            if (width != TextureWidth || height != TextureHeight)
                return null;

            // Create ImageSharp image from decoded pixels
            using var image = Image.LoadPixelData<Bgra32>(pixels, width, height);

            // Encode as 128x128 icon
            return EncodeFromImage(image, IconWidth, IconHeight);
        }
    }
}
