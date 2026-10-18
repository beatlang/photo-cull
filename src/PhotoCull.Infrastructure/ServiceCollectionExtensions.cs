using Microsoft.Extensions.DependencyInjection;
using PhotoCull.Core;
using PhotoCull.Core.Abstractions;
using PhotoCull.Infrastructure.Metadata;
using PhotoCull.Infrastructure.Quality;
using PhotoCull.Infrastructure.Storage;
using PhotoCull.Infrastructure.Pipeline;
using PhotoCull.Infrastructure.Deletion;

namespace PhotoCull.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPhotoCull(this IServiceCollection services)
        {
            // Core infrastructure implementations
            services.AddSingleton<IFolderScanner, FolderScanner>();
            services.AddSingleton<IThumbnailService, ThumbnailService>();
            services.AddSingleton<IExifReader, ExifReader>();
            services.AddSingleton<IHashService, HashService>();
            services.AddSingleton<IQualityAnalyzer, QualityAnalyzer>();
            services.AddSingleton<IImageRepository, SqliteImageRepository>();
            services.AddSingleton<CullingPipeline>();
            // file operations + deletion service for safe deletes and undo
            services.AddSingleton<PhotoCull.Core.Abstractions.IDeletionService, DeletionService>();
            services.AddSingleton<IFileOperations, WindowsFileOperations>();

            // Optional AI/embeddings are registered in PhotoCull.Ai if available
            return services;
        }
    }
}
