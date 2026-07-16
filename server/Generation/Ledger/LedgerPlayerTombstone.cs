using System.Security.Cryptography;
using System.Text;

namespace Lingmai.RedMist.Generation.Ledger;

internal static class LedgerPlayerTombstone
{
    public static string Hash(string playerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(playerId))).ToLowerInvariant()}";
    }
}
