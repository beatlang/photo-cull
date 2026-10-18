using System.Collections.ObjectModel;

namespace PhotoCull.App.ViewModels
{
    public sealed class GroupingMatchViewModel
    {
        public string FileName { get; }
        public ObservableCollection<GroupingCriterionViewModel> Criteria { get; } = new();

        public GroupingMatchViewModel(string fileName)
        {
            FileName = fileName;
        }
    }

    public sealed class GroupingCriterionViewModel
    {
        public string Name { get; }
        public string Measurement { get; }
        public string Status { get; }
        public bool IsAvailable { get; }
        public bool IsMatch { get; }

        public GroupingCriterionViewModel(string name, string measurement, bool isAvailable, bool isMatch, string? status = null)
        {
            Name = name;
            Measurement = measurement;
            IsAvailable = isAvailable;
            IsMatch = isMatch;
            Status = status ?? (!isAvailable ? "Unavailable" : isMatch ? "Connected" : "Not matched");
        }
    }
}
