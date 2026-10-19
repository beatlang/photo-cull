using Microsoft.Extensions.DependencyInjection;
using PCull.Core;
using PCull.Core.Abstractions;
using PCull.Infrastructure.Metadata;
using PCull.Infrastructure.Quality;
using PCull.Infrastructure.Storage;
using PCull.Infrastructure.Pipeline;
using PCull.Infrastructure.Deletion;

namespace PCull.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPCull(this IServiceCollection services)
        {
            // Core infrastructure implementations
            services.AddSingleton<IFolderScanner, FolderScanner>();
            services.AddSingleton<PCull.Core.Abstractions.IImageDecoder, PCull.Infrastructure.Imaging.MagickImageDecoder>();
            services.AddSingleton<IThumbnailService>(sp => new ThumbnailService(512, sp.GetRequiredService<PCull.Core.Abstractions.IImageDecoder>()));
            services.AddSingleton<IExifReader, ExifReader>();
            services.AddSingleton<IHashService>(sp => new HashService(sp.GetService<PCull.Core.Abstractions.IImageDecoder>()));
            services.AddSingleton<IQualityAnalyzer>(sp => new QualityAnalyzer(sp.GetService<PCull.Core.Abstractions.IImageDecoder>()));
            services.AddSingleton<IImageRepository>(_ =>
            {
                var dir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "pCull");
                System.IO.Directory.CreateDirectory(dir);
                return new SqliteImageRepository($"Data Source={System.IO.Path.Combine(dir, "pcull.db")}");
            });
            services.AddSingleton<CullingPipeline>();
            // file operations + deletion service for safe deletes and undo
            services.AddSingleton<PCull.Core.Abstractions.IDeletionService, DeletionService>();
            services.AddSingleton<IFileOperations, WindowsFileOperations>();

            // Optional AI/embeddings are registered in PCull.Ai if available
            return services;
        }
    }
}
