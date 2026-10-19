using System.IO;
using System.Threading.Tasks;

namespace PCull.Core.Abstractions
{
    public interface IQualityAnalyzer
    {
        Task<PCull.Core.Models.QualityMetrics> AnalyzeAsync(Stream imageStream);
    }
}
