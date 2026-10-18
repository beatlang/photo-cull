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
            services.AddSingleton<PhotoCull.Core.Abstractions.IImageDecoder, PhotoCull.Infrastructure.Imaging.MagickImageDecoder>();
            services.AddSingleton<IThumbnailService>(sp => new ThumbnailService(512, sp.GetRequiredService<PhotoCull.Core.Abstractions.IImageDecoder>()));
            services.AddSingleton<IExifReader, ExifReader>();
            services.AddSingleton<IHashService>(sp => new HashService(sp.GetService<PhotoCull.Core.Abstractions.IImageDecoder>()));
            services.AddSingleton<IQualityAnalyzer>(sp => new QualityAnalyzer(sp.GetService<PhotoCull.Core.Abstractions.IImageDecoder>()));
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
