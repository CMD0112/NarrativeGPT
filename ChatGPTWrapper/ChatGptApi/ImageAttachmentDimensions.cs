using System.Buffers.Binary;

namespace ChatGPTWrapper.ChatGptApi;

internal static class ImageAttachmentDimensions
{
    internal static (int Width, int Height)? Resolve(
        byte[] content,
        string mimeType,
        int? width = null,
        int? height = null)
    {
        if (width is > 0 && height is > 0)
            return (width.Value, height.Value);

        return TryParse(content, mimeType);
    }

    internal static (int Width, int Height)? TryParse(byte[] content, string mimeType)
    {
        if (content.Length < 10)
            return null;

        if (mimeType.StartsWith("image/png", StringComparison.OrdinalIgnoreCase)
            || (content[0] == 0x89 && content[1] == (byte)'P'))
        {
            return TryParsePng(content);
        }

        if (mimeType.StartsWith("image/gif", StringComparison.OrdinalIgnoreCase)
            || (content[0] == (byte)'G' && content[1] == (byte)'I'))
        {
            return TryParseGif(content);
        }

        if (mimeType.StartsWith("image/webp", StringComparison.OrdinalIgnoreCase)
            || (content.Length >= 12
                && content[0] == (byte)'R'
                && content[1] == (byte)'I'
                && content[8] == (byte)'W'
                && content[9] == (byte)'E'))
        {
            return TryParseWebp(content);
        }

        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || content[0] == 0xFF)
        {
            return TryParseJpeg(content);
        }

        return null;
    }

    private static (int Width, int Height)? TryParsePng(byte[] content)
    {
        if (content.Length < 24
            || content[0] != 0x89
            || content[1] != (byte)'P'
            || content[2] != (byte)'N'
            || content[3] != (byte)'G')
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(20, 4));
        return width > 0 && height > 0 ? (width, height) : null;
    }

    private static (int Width, int Height)? TryParseGif(byte[] content)
    {
        if (content.Length < 10
            || content[0] != (byte)'G'
            || content[1] != (byte)'I'
            || content[2] != (byte)'F')
        {
            return null;
        }

        var width = content[6] | (content[7] << 8);
        var height = content[8] | (content[9] << 8);
        return width > 0 && height > 0 ? (width, height) : null;
    }

    private static (int Width, int Height)? TryParseWebp(byte[] content)
    {
        if (content.Length < 30)
            return null;

        var chunk = content.AsSpan(12, 4);
        if (chunk[0] == (byte)'V' && chunk[1] == (byte)'P' && chunk[2] == (byte)'8')
        {
            if (chunk[3] == (byte)' ')
            {
                var width = content[26] | (content[27] << 8);
                var height = content[28] | (content[29] << 8);
                return width > 0 && height > 0 ? (width, height) : null;
            }

            if (chunk[3] == (byte)'L' && content.Length >= 25)
            {
                var bits = content[21] | (content[22] << 8) | (content[23] << 16) | (content[24] << 24);
                var width = (bits & 0x3FFF) + 1;
                var height = ((bits >> 14) & 0x3FFF) + 1;
                return width > 0 && height > 0 ? (width, height) : null;
            }
        }

        return null;
    }

    private static (int Width, int Height)? TryParseJpeg(byte[] content)
    {
        var i = 0;
        while (i + 9 < content.Length)
        {
            if (content[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = content[i + 1];
            if (marker is 0xD8 or 0xD9 or 0x01 or >= 0xD0 and <= 0xD7)
            {
                i += 2;
                continue;
            }

            if (i + 3 >= content.Length)
                return null;

            var segmentLength = (content[i + 2] << 8) | content[i + 3];
            if (segmentLength < 2 || i + 2 + segmentLength > content.Length)
                return null;

            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                var height = (content[i + 5] << 8) | content[i + 6];
                var width = (content[i + 7] << 8) | content[i + 8];
                return width > 0 && height > 0 ? (width, height) : null;
            }

            i += 2 + segmentLength;
        }

        return null;
    }
}
