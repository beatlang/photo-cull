using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCull.Core;
using PCull.Core.Abstractions;
using PCull.Core.Culling;
using PCull.Infrastructure.Pipeline;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace PCull.App.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly CullingPipeline _pipeline;
        private readonly IImageRepository _repo;
        private readonly IThumbnailService _thumb;
        private readonly IDeletionService _deleter;
        private ClusterOptions _clusterOptions = new();
        private CancellationTokenSource? _cts;

        public ObservableCollection<SetViewModel> Sets { get; } = new();

        private SetViewModel? _selectedSet;
        public SetViewModel? SelectedSet
        {
            get => _selectedSet;
            set
            {
                if (SetProperty(ref _selectedSet, value))
                {
                    SelectedImage = value?.Members.FirstOrDefault(m => m.IsBestShot) ?? value?.Members.FirstOrDefault();
                    UpdateSelectedImageMatchReasons();
                }
            }
        }

        private ImageViewModel? _selectedImage;
        public ImageViewModel? SelectedImage
        {
            get => _selectedImage;
            set
            {
                if (SetProperty(ref _selectedImage, value))
                    UpdateSelectedImageMatchReasons();
            }
        }

        public ObservableCollection<GroupingMatchViewModel> SelectedImageMatchReasons { get; } = new();
        public string MatchDetailsStatus { get; private set; } = "Select a photo in a set to see why it was grouped.";

        private double _progress;
        public double Progress { get => _progress; set => SetProperty(ref _progress, value); }

        private bool _isScanning;
        public bool IsScanning { get => _isScanning; set => SetProperty(ref _isScanning, value); }

        private int _hiddenSetCount;
        public int HiddenSetCount { get => _hiddenSetCount; private set => SetProperty(ref _hiddenSetCount, value); }

        private string _summary = string.Empty;
        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

        private int _markedForDeleteCount;
        public int MarkedForDeleteCount { get => _markedForDeleteCount; private set => SetProperty(ref _markedForDeleteCount, value); }

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public MainWindowViewModel(CullingPipeline pipeline, IImageRepository repo, IThumbnailService thumb, IDeletionService deleter)
        {
            _pipeline = pipeline; _repo = repo; _thumb = thumb; _deleter = deleter;
        }

        [RelayCommand]
        public async Task StartScanAsync(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            try
            {
                IsScanning = true;
                _cts = new CancellationTokenSource();
                var cache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pCull","cache");
                var progress = new Progress<PCull.Infrastructure.Pipeline.PipelineProgress>(p => {
                    var completed = p.Processed + p.Errors;
                    Progress = p.Scanned > 0 ? completed * 100d / p.Scanned : 0;
                    StatusText = $"Scanning... {completed}/{p.Scanned}";
                });

                await _pipeline.RunAsync(folder, cache, progress, _cts.Token, _clusterOptions);
                await LoadSetsAsync();
                StatusText = "Scan complete";
            }
            catch (OperationCanceledException) { }
            finally { IsScanning = false; _cts = null; }
        }

        [RelayCommand]
        public void CancelScan()
        {
            _cts?.Cancel();
        }

        public void UpdateClusterOptions(ClusterOptions options)
        {
            _clusterOptions = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task LoadSetsAsync()
        {
            foreach (var set in Sets)
                Unsubscribe(set);
            Sets.Clear();
            HiddenSetCount = 0;
            var sets = await _repo.ListSetsAsync();
            var cache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pCull","cache");
            foreach (var s in sets)
            {
                var members = (await _repo.LoadSetMembersAsync(s.Id)).ToList();
                if (members.Count < 2 || members.All(m => m.Recommendation == Recommendation.Keep))
                {
                    HiddenSetCount++;
                    continue;
                }

                var svm = new SetViewModel(s.Id, $"Set {Sets.Count + 1} · {members.Count} photos");
                foreach (var match in await _repo.LoadSetMatchesAsync(s.Id))
                    svm.Matches.Add(match);
                foreach (var m in members)
                {
                    try
                    {
                        var rec = await _repo.GetImageByIdAsync(m.ImageId);
                        if (rec == null) continue;
                        var ivm = new ImageViewModel(rec);
                        ivm.Recommendation = m.Recommendation;
                        ivm.IsBestShot = m.IsBestShot;
                        await ivm.LoadExtrasAsync(_thumb, _repo, cache);
                        ivm.PropertyChanged += ImagePropertyChanged;
                        svm.Members.Add(ivm);
                    }
                    catch { }
                }
                if (svm.Members.Count >= 2)
                    Sets.Add(svm);
                else
                    HiddenSetCount++;
            }

            SelectedSet = Sets.FirstOrDefault();
            UpdateSummary();
        }

        private void ImagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ImageViewModel.UserDecision))
                UpdateSummary();
        }

        private void Unsubscribe(SetViewModel set)
        {
            foreach (var member in set.Members)
                member.PropertyChanged -= ImagePropertyChanged;
        }

        private void UpdateSummary()
        {
            MarkedForDeleteCount = Sets.SelectMany(s => s.Members)
                .Count(m => string.Equals(m.UserDecision, "Delete", StringComparison.OrdinalIgnoreCase));
            var count = Sets.SelectMany(s => s.Members)
                .Count(m => m.Recommendation == Recommendation.Delete);
            Summary = $"{Sets.Count} sets to review · {count} photos recommended for deletion";
        }

        private void UpdateSelectedImageMatchReasons()
        {
            SelectedImageMatchReasons.Clear();
            if (SelectedImage == null || SelectedSet == null)
            {
                MatchDetailsStatus = "Select a photo in a set to see why it was grouped.";
                OnPropertyChanged(nameof(MatchDetailsStatus));
                return;
            }

            foreach (var match in SelectedSet.Matches.Where(m => m.ImageId == SelectedImage.Id || m.MatchedImageId == SelectedImage.Id))
            {
                var otherId = match.ImageId == SelectedImage.Id ? match.MatchedImageId : match.ImageId;
                var other = SelectedSet.Members.FirstOrDefault(member => member.Id == otherId);
                if (other == null) continue;

                bool hasEvaluationDetails = match.EvaluatedCriteria != ClusterCriterionEvaluation.None;
                var explanation = new GroupingMatchViewModel(other.FileName);
                explanation.Criteria.Add(CreateCriterion(
                    "Capture time",
                    match.CaptureTimeDifferenceSeconds,
                    match.BurstWindowSeconds,
                    hasEvaluationDetails,
                    match.EvaluatedCriteria.HasFlag(ClusterCriterionEvaluation.CaptureTime),
                    "<=",
                    (difference, threshold) => difference <= threshold,
                    "s"));
                explanation.Criteria.Add(CreateCriterion(
                    "dHash",
                    match.DHashDistance,
                    match.DHashThreshold,
                    hasEvaluationDetails,
                    match.EvaluatedCriteria.HasFlag(ClusterCriterionEvaluation.DHash),
                    "<=",
                    (distance, threshold) => distance <= threshold,
                    "bits"));
                explanation.Criteria.Add(CreateCriterion(
                    "pHash",
                    match.PHashDistance,
                    match.PHashThreshold,
                    hasEvaluationDetails,
                    match.EvaluatedCriteria.HasFlag(ClusterCriterionEvaluation.PHash),
                    "<=",
                    (distance, threshold) => distance <= threshold,
                    "bits"));
                explanation.Criteria.Add(CreateCriterion(
                    "AI similarity",
                    match.EmbeddingCosineSimilarity,
                    match.EmbeddingCosineThreshold,
                    hasEvaluationDetails,
                    match.EvaluatedCriteria.HasFlag(ClusterCriterionEvaluation.Embedding),
                    ">=",
                    (similarity, threshold) => similarity >= threshold,
                    string.Empty));
                SelectedImageMatchReasons.Add(explanation);
            }

            MatchDetailsStatus = SelectedImageMatchReasons.Count == 0
                ? "No match details stored. Scan the folder again to calculate them."
                : "This photo is directly connected to the following set members:";
            OnPropertyChanged(nameof(MatchDetailsStatus));
        }

        private static GroupingCriterionViewModel CreateCriterion(
            string name,
            double? value,
            double? threshold,
            bool hasEvaluationDetails,
            bool evaluated,
            string comparison,
            Func<double, double, bool> matches,
            string unit)
        {
            if (!hasEvaluationDetails)
                return new GroupingCriterionViewModel(name, "Rescan to calculate", false, false, "Rescan needed");
            if (!evaluated)
                return new GroupingCriterionViewModel(name, "Skipped after earlier match", false, false, "Skipped");
            if (!threshold.HasValue)
                return new GroupingCriterionViewModel(name, "Threshold unavailable", false, false, "Unavailable");
            if (!value.HasValue)
                return new GroupingCriterionViewModel(name, "Not available", false, false);

            var measurement = string.IsNullOrEmpty(unit)
                ? $"{value.Value.ToString("0.##", CultureInfo.CurrentCulture)} {comparison} {threshold.Value.ToString("0.##", CultureInfo.CurrentCulture)}"
                : $"{value.Value.ToString("0.##", CultureInfo.CurrentCulture)} {unit} {comparison} {threshold.Value.ToString("0.##", CultureInfo.CurrentCulture)} {unit}";
            return new GroupingCriterionViewModel(name, measurement, true, matches(value.Value, threshold.Value));
        }

        [RelayCommand]
        public void SelectImage(ImageViewModel image)
        {
            if (image != null && SelectedSet?.Members.Contains(image) == true)
                SelectedImage = image;
        }

        [RelayCommand]
        public void NextImage()
        {
            if (SelectedSet == null || SelectedSet.Members.Count == 0) return;
            var index = SelectedImage == null ? -1 : SelectedSet.Members.IndexOf(SelectedImage);
            SelectedImage = SelectedSet.Members[(index + 1) % SelectedSet.Members.Count];
        }

        [RelayCommand]
        public void PreviousImage()
        {
            if (SelectedSet == null || SelectedSet.Members.Count == 0) return;
            var index = SelectedImage == null ? 0 : SelectedSet.Members.IndexOf(SelectedImage);
            SelectedImage = SelectedSet.Members[(index - 1 + SelectedSet.Members.Count) % SelectedSet.Members.Count];
        }

        [RelayCommand]
        public void NextSet()
        {
            if (Sets.Count == 0) return;
            var index = SelectedSet == null ? -1 : Sets.IndexOf(SelectedSet);
            SelectedSet = Sets[(index + 1) % Sets.Count];
        }

        [RelayCommand]
        public void PreviousSet()
        {
            if (Sets.Count == 0) return;
            var index = SelectedSet == null ? 0 : Sets.IndexOf(SelectedSet);
            SelectedSet = Sets[(index - 1 + Sets.Count) % Sets.Count];
        }

        [RelayCommand]
        public async Task MarkDecisionAsync(string action)
        {
            if (SelectedImage == null) return;
            await _repo.SaveUserDecisionAsync(SelectedImage.Id, action);
            var currentIndex = SelectedSet?.Members.IndexOf(SelectedImage) ?? -1;
            SelectedImage.UserDecision = action;
            var members = SelectedSet?.Members;
            var next = members == null
                ? null
                : members.Skip(currentIndex + 1).Concat(members.Take(currentIndex + 1))
                    .FirstOrDefault(m => string.IsNullOrEmpty(m.UserDecision));
            if (next != null) SelectedImage = next;
        }

        [RelayCommand]
        public async Task AcceptRecommendationsForSet()
        {
            if (SelectedSet == null) return;
            foreach (var image in SelectedSet.Members)
            {
                var decision = image.Recommendation.ToString();
                await _repo.SaveUserDecisionAsync(image.Id, decision);
                image.UserDecision = decision;
            }
        }

        public async Task<PCull.Core.Abstractions.DeletionPreview> PreviewDeleteAsync()
        {
            var toDelete = Sets.SelectMany(s => s.Members)
                .Where(m => string.Equals(m.UserDecision, "Delete", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Path).ToList();
            return await _deleter.PreviewAsync(toDelete);
        }

        [RelayCommand]
        public async Task ExecuteDeleteAsync(bool recycle)
        {
            var toDelete = Sets.SelectMany(s => s.Members)
                .Where(m => string.Equals(m.UserDecision, "Delete", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Path).ToList();
            if (toDelete.Count == 0) return;
            var mode = recycle ? PCull.Core.Abstractions.DeletionMode.Recycle : PCull.Core.Abstractions.DeletionMode.Permanent;
            var report = await _deleter.ExecuteAsync(toDelete, mode);
            foreach (var p in report.Succeeded)
            {
                foreach (var s in Sets)
                {
                    var found = s.Members.FirstOrDefault(x => x.Path == p);
                    if (found != null)
                    {
                        found.PropertyChanged -= ImagePropertyChanged;
                        s.Members.Remove(found);
                    }
                }
            }
            foreach (var s in Sets.Where(s => s.Members.Count == 0).ToList())
                Sets.Remove(s);
            if (SelectedSet == null || !Sets.Contains(SelectedSet))
                SelectedSet = Sets.FirstOrDefault();
            UpdateSummary();
        }
    }
}