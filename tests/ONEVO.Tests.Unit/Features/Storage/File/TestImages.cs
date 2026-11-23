using System.Buffers.Binary;
using SkiaSharp;

namespace ONEVO.Tests.Unit.Features.Storage.File;

/// <summary>Real, decodable test images, including a JPEG carrying GPS EXIF like a phone photo.</summary>
internal static class TestImages
{
    public static byte[] Encode(SKEncodedImageFormat format, int width, int height, int quality = 90)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
            using var paint = new SKPaint { Color = SKColors.OrangeRed };
            canvas.DrawRect(0, 0, width / 2f, height / 2f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    /// <summary>
    /// A JPEG with an EXIF APP1 segment holding an Orientation tag and a GPS IFD
    /// (latitude 6°55'N, longitude 79°51'E), inserted straight after the SOI marker.
    /// </summary>
    public static byte[] JpegWithGpsExif(int width, int height, ushort orientation = 1)
    {
        var jpeg = Encode(SKEncodedImageFormat.Jpeg, width, height);
        var tiff = BuildTiffWithGps(orientation);

        var app1Length = 2 + ExifHeader.Length + tiff.Length; // the length field counts itself
        var app1 = new byte[2 + app1Length];
        app1[0] = 0xFF;
        app1[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(app1.AsSpan(2), (ushort)app1Length);
        ExifHeader.CopyTo(app1, 4);
        tiff.CopyTo(app1, 4 + ExifHeader.Length);

        var output = new byte[jpeg.Length + app1.Length];
        jpeg.AsSpan(0, 2).CopyTo(output);                 // SOI
        app1.CopyTo(output, 2);
        jpeg.AsSpan(2).CopyTo(output.AsSpan(2 + app1.Length));
        return output;
    }

    public static readonly byte[] ExifHeader = "Exif\0\0"u8.ToArray();

    public static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    private static byte[] BuildTiffWithGps(ushort orientation)
    {
        // Little-endian TIFF: header (8) | IFD0 at 8 (2 entries) | GPS IFD at 38 (4 entries) | rationals at 92.
        const int ifd0Offset = 8;
        const int gpsIfdOffset = ifd0Offset + 2 + (2 * 12) + 4;       // 38
        const int rationalsOffset = gpsIfdOffset + 2 + (4 * 12) + 4;  // 92
        var tiff = new byte[rationalsOffset + 48];
        var span = tiff.AsSpan();

        "II"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], ifd0Offset);

        var p = ifd0Offset;
        BinaryPrimitives.WriteUInt16LittleEndian(span[p..], 2); p += 2;
        p = Entry(span, p, tag: 0x0112, type: 3, count: 1, value: orientation);       // Orientation (SHORT)
        p = Entry(span, p, tag: 0x8825, type: 4, count: 1, value: gpsIfdOffset);      // GPSInfo IFD pointer
        BinaryPrimitives.WriteUInt32LittleEndian(span[p..], 0);

        p = gpsIfdOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(span[p..], 4); p += 2;
        p = Entry(span, p, tag: 0x0001, type: 2, count: 2, value: 'N');                // GPSLatitudeRef "N\0"
        p = Entry(span, p, tag: 0x0002, type: 5, count: 3, value: rationalsOffset);    // GPSLatitude
        p = Entry(span, p, tag: 0x0003, type: 2, count: 2, value: 'E');                // GPSLongitudeRef "E\0"
        p = Entry(span, p, tag: 0x0004, type: 5, count: 3, value: rationalsOffset + 24); // GPSLongitude
        BinaryPrimitives.WriteUInt32LittleEndian(span[p..], 0);

        WriteRationals(span[rationalsOffset..], 6, 55, 0);
        WriteRationals(span[(rationalsOffset + 24)..], 79, 51, 0);
        return tiff;
    }

    private static int Entry(Span<byte> span, int offset, ushort tag, ushort type, uint count, uint value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], tag);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(offset + 2)..], type);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 4)..], count);
        if (type == 3)
            BinaryPrimitives.WriteUInt16LittleEndian(span[(offset + 8)..], (ushort)value);
        else if (type == 2)
            span[offset + 8] = (byte)value; // inline ASCII, NUL-terminated by the zeroed buffer
        else
            BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 8)..], value);
        return offset + 12;
    }

    private static void WriteRationals(Span<byte> span, uint degrees, uint minutes, uint seconds)
    {
        uint[] values = [degrees, 1, minutes, 1, seconds, 1];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(span[(i * 4)..], values[i]);
    }
}
