using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheArchitect.TheArchitectCode.Challenger;

/// <summary>Schema 1: SHA-256 seed tuple, SplitMix64 stream, unbiased Fisher-Yates. No game RNG.</summary>
internal sealed class PrivateRng(ulong state)
{
    public ulong State { get; private set; } = state;

    public static PrivateRng Create(string runSeed, string snapshotIdentity, string encounterIdentity)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new[] { "challenger-rng-v1", runSeed, snapshotIdentity, encounterIdentity })));
        return new(BinaryPrimitives.ReadUInt64LittleEndian(hash));
    }

    private ulong Next()
    {
        unchecked
        {
            State += 0x9E3779B97F4A7C15UL;
            ulong z = State;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    public void Shuffle<T>(IList<T> values)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            ulong bound = (ulong)i + 1;
            ulong threshold = unchecked(0UL - bound) % bound;
            ulong random;
            do { random = Next(); } while (random < threshold);
            int j = (int)(random % bound);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }
}
