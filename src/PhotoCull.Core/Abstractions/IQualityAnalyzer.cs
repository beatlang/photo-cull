using System.IO;
using System.Threading.Tasks;

namespace PhotoCull.Core.Abstractions
{
    public interface IQualityAnalyzer
    {
        Task<PhotoCull.Core.Models.QualityMetrics> AnalyzeAsync(Stream imageStream);
    }
}
