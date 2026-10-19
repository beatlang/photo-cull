using CommunityToolkit.Mvvm.ComponentModel;
using PCull.Core;
using PCull.Core.Abstractions;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PCull.App.ViewModels
{
    public class ImageViewModel : ObservableObject
    {
        public long Id { get; }
        public string Path { get; }
        public long SizeBytes { get; }
        public int Width { get; }
        public int Height { get; }

        private string? _thumbnailPath;
        public string? ThumbnailPath
        {
            get => _thumbnailPath;
            set
            {
                if (SetProperty(ref _thumbnailPath, value))
                    OnPropertyChanged(nameof(ThumbnailUri));
            }
        }

        private Recommendation _recommendation;
        public Recommendation Recommendation { get => _recommendation; set => SetProperty(ref _recommendation, value); }

        private bool _isBestShot;
        public bool IsBestShot { get => _isBestShot; set => SetProperty(ref _isBestShot, value); }

        public string ThumbnailUri => ThumbnailPath ?? Path;
        public string FileName => System.IO.Path.GetFileName(Path);
        public string InfoLine => $"{Width}x{Height} · {SizeBytes / (1024d * 1024d):F1} MB";
        public string Badge => EffectiveDecision;
        public string EffectiveDecision => UserDecision ?? Recommendation.ToString();

        private QualityScores? _scores;
        public QualityScores? Scores { get => _scores; set => SetProperty(ref _scores, value); }

        private string? _userDecision;
        public string? UserDecision
        {
            get => _userDecision;
            set
            {
                if (SetProperty(ref _userDecision, value))
                {
                    OnPropertyChanged(nameof(EffectiveDecision));
                    OnPropertyChanged(nameof(Badge));
                }
            }
        }

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