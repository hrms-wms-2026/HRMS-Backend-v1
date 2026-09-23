using System.Security.Cryptography;
using System.Text;

namespace ONEVO.Application.Common.Helpers;

/// <summary>RFC 4122 version-5 (SHA-1 name-based) UUID generation - the same (namespaceId, name)
/// pair always produces the same GUID. Used to give a virtual recurring-event occurrence a stable
/// id derived from (masterEventId, occurrenceStart) without persisting a row for it.</summary>
public static class DeterministicGuid
{
    public static Guid Create(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);

        using var sha1 = SHA1.Create();
        sha1.TransformBlock(namespaceBytes, 0, namespaceBytes.Length, null, 0);
        sha1.TransformFinalBlock(nameBytes, 0, nameBytes.Length);
        var hash = sha1.Hash!;

        var newGuid = new byte[16];
        Array.Copy(hash, 0, newGuid, 0, 16);

        newGuid[6] = (byte)((newGuid[6] & 0x0F) | (5 << 4)); // version 5
        newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80); // RFC 4122 variant

        SwapByteOrder(newGuid);
        return new Guid(newGuid);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        SwapBytes(guid, 0, 3);
        SwapBytes(guid, 1, 2);
        SwapBytes(guid, 4, 5);
        SwapBytes(guid, 6, 7);
    }

    private static void SwapBytes(byte[] guid, int left, int right)
        => (guid[left], guid[right]) = (guid[right], guid[left]);
}
