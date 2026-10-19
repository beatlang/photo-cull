using System;

namespace PCull.Core
{
    public record ImageRecord(
        long Id,
        string Path,
        long SizeBytes,
        int Width,
        int Height,
        string Format,
        DateTime CreatedUtc,
        DateTime ModifiedUtc);

    public enum Recommendation
    {
        Keep,
        Review,
        Delete
    }
}
