using System;
using System.IO;
using System.Text;

namespace CsvViewer.Services;

public sealed class EncodingDetectionService
{
    public EncodingDetectionService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public Encoding Detect(string filePath)
    {
        var buffer = new byte[Math.Min(65536, Math.Max(0, (int)new FileInfo(filePath).Length))];
        using (var stream = File.OpenRead(filePath))
        {
            _ = stream.Read(buffer, 0, buffer.Length);
        }

        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        if (buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (buffer.Length >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        if (IsUtf8(buffer))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        return Encoding.GetEncoding("GB18030");
    }

    private static bool IsUtf8(byte[] bytes)
    {
        try
        {
            var strictUtf8 = new UTF8Encoding(false, true);
            _ = strictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
