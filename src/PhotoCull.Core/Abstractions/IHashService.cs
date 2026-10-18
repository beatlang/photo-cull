using System.IO;

namespace PhotoCull.Core.Abstractions
{
    public interface IHashService
    {
        // Compute 64-bit average hash
        ulong ComputeAHash(Stream stream);

        // Compute 64-bit difference hash
        ulong ComputeDHash(Stream stream);

        // Compute 64-bit perceptual hash (pHash) using 32x32 DCT -> 8x8 coefficients
        ulong ComputePHash(Stream stream);

        // Hamming distance between two 64-bit hashes
        int HammingDistance(ulong a, ulong b);

        // Determine near-duplicate using a threshold
        bool IsNearDuplicate(ulong a, ulong b, int maxDistance);
    }
}
