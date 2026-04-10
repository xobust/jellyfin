using System;
using System.IO;
using NEbml.Core;

namespace Jellyfin.MediaEncoding.Keyframes.Matroska;

/// <summary>
/// Removes EBML Void (0xEC) elements from a Matroska <c>Chapters</c> element <strong>inner payload</strong> so NEbml can
/// parse padding that libmatroska/mkvtoolnix embed (e.g. Void after <c>ChapLanguage</c>).
/// </summary>
internal static class MatroskaChapterVoidStripper
{
    /// <summary>
    /// Recursively strips Void elements from the inner payload of a Chapters master (bytes after Chapters ID + size).
    /// </summary>
    /// <param name="chaptersInnerPayload">Raw bytes inside the Chapters element (excluding element ID and size).</param>
    /// <returns>New buffer with Void elements removed; masters re-encoded with updated sizes.</returns>
    public static byte[] StripVoidFromChaptersInnerPayload(byte[] chaptersInnerPayload)
    {
        ArgumentNullException.ThrowIfNull(chaptersInnerPayload);
        return StripRecursive(chaptersInnerPayload);
    }

    private static byte[] StripRecursive(ReadOnlySpan<byte> span)
    {
        if (span.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var output = new MemoryStream(span.Length);
        var pos = 0;
        while (pos < span.Length)
        {
            if (!TryReadElementBounds(span, pos, out var id, out _, out var headerLen, out var dataLen, out var totalLen))
            {
                throw new EbmlDataFormatException("Invalid EBML while stripping Void elements in Chapters payload");
            }

            if (IsEbmlVoid(id))
            {
                pos += totalLen;
                continue;
            }

            if (ShouldRecurseIntoPayload(id) && dataLen > 0)
            {
                var inner = span.Slice(pos + headerLen, dataLen);
                var strippedInner = StripRecursive(inner);
                WriteMasterElement(output, id, strippedInner);
            }
            else
            {
                output.Write(span.Slice(pos, totalLen));
            }

            pos += totalLen;
        }

        return output.ToArray();
    }

    private static bool ShouldRecurseIntoPayload(VInt id)
    {
        var v = id.EncodedValue;
        return v == MatroskaConstants.EditionEntry
               || v == MatroskaConstants.ChapterAtom
               || v == MatroskaConstants.ChapterDisplay
               || v == 0x2924UL // ChapterProcess
               || v == 0x6924UL; // ChapterTranslate
    }

    private static bool IsEbmlVoid(VInt id) => id.Length == 1 && (id.EncodedValue & 0xFF) == 0xEC;

    private static bool TryReadElementBounds(
        ReadOnlySpan<byte> span,
        int start,
        out VInt id,
        out VInt size,
        out int headerLength,
        out int dataLength,
        out int totalLength)
    {
        id = default;
        size = default;
        headerLength = 0;
        dataLength = 0;
        totalLength = 0;

        if (start >= span.Length)
        {
            return false;
        }

        using var ms = new MemoryStream(span[start..].ToArray(), writable: false);
        var buffer = new byte[16];
        id = VInt.Read(ms, 4, buffer);
        size = VInt.Read(ms, 8, buffer);
        headerLength = (int)ms.Position;

        if (size.IsReserved)
        {
            return false;
        }

        dataLength = (int)size.Value;
        if (dataLength < 0 || start + headerLength + dataLength > span.Length)
        {
            return false;
        }

        totalLength = headerLength + dataLength;
        return true;
    }

    private static void WriteMasterElement(MemoryStream output, VInt id, byte[] innerPayload)
    {
        using var header = new MemoryStream(32);
        id.Write(header);
        var size = VInt.EncodeSize((ulong)innerPayload.Length);
        size.Write(header);
        header.WriteTo(output);
        output.Write(innerPayload, 0, innerPayload.Length);
    }
}
