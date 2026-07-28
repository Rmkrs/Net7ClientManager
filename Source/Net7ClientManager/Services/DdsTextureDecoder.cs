namespace Net7ClientManager.Services;

using System.Buffers.Binary;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

/// <summary>
/// Minimal decoder for the DDS formats used by Earth & Beyond's UI artwork.
/// The client art archives currently use DXT1 and DXT5; only the top mip is
/// needed for compact command-palette presentation.
/// </summary>
internal static class DdsTextureDecoder
{
    private const uint DdsMagic = 0x20534444;
    private const uint Dxt1FourCc = 0x31545844;
    private const uint Dxt5FourCc = 0x35545844;
    private const int DdsHeaderLength = 128;

    public static Bitmap? DecodeTopMip(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < DdsHeaderLength ||
            ReadUInt32(bytes, 0) != DdsMagic ||
            ReadUInt32(bytes, 4) != 124 ||
            ReadUInt32(bytes, 76) != 32)
        {
            return null;
        }

        var height = checked((int)ReadUInt32(bytes, 12));
        var width = checked((int)ReadUInt32(bytes, 16));
        var fourCc = ReadUInt32(bytes, 84);

        if (width <= 0 ||
            height <= 0 ||
            width > 16_384 ||
            height > 16_384)
        {
            return null;
        }

        var blockLength = fourCc switch
        {
            Dxt1FourCc => 8,
            Dxt5FourCc => 16,
            _ => 0,
        };

        if (blockLength == 0)
        {
            return null;
        }

        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;
        var compressedLength = checked(blocksWide * blocksHigh * blockLength);

        if (bytes.Length < DdsHeaderLength + compressedLength)
        {
            return null;
        }

        var pixels = new byte[checked(width * height * 4)];
        var sourceOffset = DdsHeaderLength;

        for (var blockY = 0; blockY < blocksHigh; blockY++)
        {
            for (var blockX = 0; blockX < blocksWide; blockX++)
            {
                var block = bytes.Slice(sourceOffset, blockLength);

                if (fourCc == Dxt1FourCc)
                {
                    DecodeDxt1Block(
                        block,
                        pixels,
                        width,
                        height,
                        blockX * 4,
                        blockY * 4);
                }
                else
                {
                    DecodeDxt5Block(
                        block,
                        pixels,
                        width,
                        height,
                        blockX * 4,
                        blockY * 4);
                }

                sourceOffset += blockLength;
            }
        }

        return CreateBitmap(width, height, pixels);
    }

    public static Bitmap CreatePresentationBitmap(
        Image source,
        Size size,
        float? tintRed,
        float? tintGreen,
        float? tintBlue)
    {
        var width = Math.Max(1, size.Width);
        var height = Math.Max(1, size.Height);
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(result);
        using var attributes = new ImageAttributes();

        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.None;

        var red = ClampTint(tintRed);
        var green = ClampTint(tintGreen);
        var blue = ClampTint(tintBlue);
        var matrix = new ColorMatrix(
            new[]
            {
                new[] { red, 0.0f, 0.0f, 0.0f, 0.0f },
                new[] { 0.0f, green, 0.0f, 0.0f, 0.0f },
                new[] { 0.0f, 0.0f, blue, 0.0f, 0.0f },
                new[] { 0.0f, 0.0f, 0.0f, 1.0f, 0.0f },
                new[] { 0.0f, 0.0f, 0.0f, 0.0f, 1.0f },
            });

        attributes.SetColorMatrix(
            matrix,
            ColorMatrixFlag.Default,
            ColorAdjustType.Bitmap);

        graphics.DrawImage(
            source,
            new Rectangle(0, 0, width, height),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel,
            attributes);

        return result;
    }

    private static float ClampTint(float? value)
    {
        if (!value.HasValue ||
            float.IsNaN(value.Value) ||
            float.IsInfinity(value.Value))
        {
            return 1.0f;
        }

        return Math.Clamp(value.Value, 0.0f, 4.0f);
    }

    private static void DecodeDxt1Block(
        ReadOnlySpan<byte> block,
        byte[] pixels,
        int width,
        int height,
        int destinationX,
        int destinationY)
    {
        var color0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var color1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        Span<DecodedColor> colors = stackalloc DecodedColor[4];
        BuildColorPalette(color0, color1, allowTransparentColor: true, colors);
        var colorIndices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);

        WriteColorBlock(
            pixels,
            width,
            height,
            destinationX,
            destinationY,
            colors,
            colorIndices,
            alphaValues: default,
            alphaIndices: 0,
            useExplicitAlpha: false);
    }

    private static void DecodeDxt5Block(
        ReadOnlySpan<byte> block,
        byte[] pixels,
        int width,
        int height,
        int destinationX,
        int destinationY)
    {
        Span<byte> alphaValues = stackalloc byte[8];
        BuildAlphaPalette(block[0], block[1], alphaValues);

        ulong alphaIndices = 0;

        for (var index = 0; index < 6; index++)
        {
            alphaIndices |= (ulong)block[2 + index] << (index * 8);
        }

        var color0 = BinaryPrimitives.ReadUInt16LittleEndian(block[8..]);
        var color1 = BinaryPrimitives.ReadUInt16LittleEndian(block[10..]);
        Span<DecodedColor> colors = stackalloc DecodedColor[4];
        BuildColorPalette(color0, color1, allowTransparentColor: false, colors);
        var colorIndices = BinaryPrimitives.ReadUInt32LittleEndian(block[12..]);

        WriteColorBlock(
            pixels,
            width,
            height,
            destinationX,
            destinationY,
            colors,
            colorIndices,
            alphaValues,
            alphaIndices,
            useExplicitAlpha: true);
    }

    private static void BuildColorPalette(
        ushort color0,
        ushort color1,
        bool allowTransparentColor,
        Span<DecodedColor> colors)
    {
        colors[0] = DecodeRgb565(color0);
        colors[1] = DecodeRgb565(color1);

        if (!allowTransparentColor || color0 > color1)
        {
            colors[2] = Interpolate(colors[0], colors[1], 2, 1, 3);
            colors[3] = Interpolate(colors[0], colors[1], 1, 2, 3);
            return;
        }

        colors[2] = Interpolate(colors[0], colors[1], 1, 1, 2);
        colors[3] = new DecodedColor(0, 0, 0, 0);
    }

    private static void BuildAlphaPalette(
        byte alpha0,
        byte alpha1,
        Span<byte> values)
    {
        values[0] = alpha0;
        values[1] = alpha1;

        if (alpha0 > alpha1)
        {
            for (var index = 1; index <= 6; index++)
            {
                values[index + 1] = (byte)(
                    ((7 - index) * alpha0 + index * alpha1) / 7);
            }

            return;
        }

        for (var index = 1; index <= 4; index++)
        {
            values[index + 1] = (byte)(
                ((5 - index) * alpha0 + index * alpha1) / 5);
        }

        values[6] = 0;
        values[7] = 255;
    }

    private static void WriteColorBlock(
        byte[] pixels,
        int width,
        int height,
        int destinationX,
        int destinationY,
        ReadOnlySpan<DecodedColor> colors,
        uint colorIndices,
        ReadOnlySpan<byte> alphaValues,
        ulong alphaIndices,
        bool useExplicitAlpha)
    {
        for (var pixelIndex = 0; pixelIndex < 16; pixelIndex++)
        {
            var x = destinationX + pixelIndex % 4;
            var y = destinationY + pixelIndex / 4;

            if (x >= width || y >= height)
            {
                continue;
            }

            var colorIndex = (int)((colorIndices >> (pixelIndex * 2)) & 0x3);
            var color = colors[colorIndex];
            var alpha = useExplicitAlpha
                ? alphaValues[(int)((alphaIndices >> (pixelIndex * 3)) & 0x7)]
                : color.Alpha;
            var destinationOffset = (y * width + x) * 4;

            pixels[destinationOffset] = color.Blue;
            pixels[destinationOffset + 1] = color.Green;
            pixels[destinationOffset + 2] = color.Red;
            pixels[destinationOffset + 3] = alpha;
        }
    }

    private static DecodedColor DecodeRgb565(ushort value)
    {
        var red = (byte)(((value >> 11) & 0x1F) * 255 / 31);
        var green = (byte)(((value >> 5) & 0x3F) * 255 / 63);
        var blue = (byte)((value & 0x1F) * 255 / 31);
        return new DecodedColor(red, green, blue, 255);
    }

    private static DecodedColor Interpolate(
        DecodedColor first,
        DecodedColor second,
        int firstWeight,
        int secondWeight,
        int divisor)
    {
        return new DecodedColor(
            (byte)((first.Red * firstWeight + second.Red * secondWeight) / divisor),
            (byte)((first.Green * firstWeight + second.Green * secondWeight) / divisor),
            (byte)((first.Blue * firstWeight + second.Blue * secondWeight) / divisor),
            255);
    }

    private static Bitmap CreateBitmap(
        int width,
        int height,
        byte[] pixels)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var bounds = new Rectangle(0, 0, width, height);
        var bitmapData = bitmap.LockBits(
            bounds,
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            var sourceStride = width * 4;

            if (bitmapData.Stride == sourceStride)
            {
                Marshal.Copy(pixels, 0, bitmapData.Scan0, pixels.Length);
                return bitmap;
            }

            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(
                    pixels,
                    row * sourceStride,
                    IntPtr.Add(bitmapData.Scan0, row * bitmapData.Stride),
                    sourceStride);
            }

            return bitmap;
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    }

    private readonly record struct DecodedColor(
        byte Red,
        byte Green,
        byte Blue,
        byte Alpha);
}
