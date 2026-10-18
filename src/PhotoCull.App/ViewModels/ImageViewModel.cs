using CommunityToolkit.Mvvm.ComponentModel;
using PhotoCull.Core;
using PhotoCull.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace PhotoCull.App.ViewModels
{
    public class ImageViewModel : ObservableObject
    {
        public long Id { get; }
        public string Path { get; }
        public long SizeBytes { get; }
        public int Width { get; }
        public int Height { get; }

        private string? _thumbnailPath;
        public string? ThumbnailPath { get => _thumbnailPath; set => SetProperty(ref _thumbnailPath, value); }

        private QualityScores? _scores;
        public QualityScores? Scores { get => _scores; set => SetProperty(ref _scores, value); }

        private string? _userDecision;
        public string? UserDecision { get => _userDecision; set => SetProperty(ref _userDecision, value); }

        public ImageViewModel(ImageRecord rec)
        {
            Id = rec.Id;
            Path = rec.Path;
            SizeBytes = rec.SizeBytes;
            Width = rec.Width;
            Height = rec.Height;
        }

        public async Task LoadExtrasAsync(IThumbnailService thumbSvc, IImageRepository repo, string cacheDir)
        {
            if (thumbSvc != null)
            {
                try { ThumbnailPath = await thumbSvc.GenerateThumbnailAsync(Path, cacheDir); } catch { }
            }

            try { Scores = await repo.LoadQualityScoresAsync(Id); } catch { }
            try { UserDecision = await repo.LoadUserDecisionAsync(Id); } catch { }
        }
    }
}