using System.Collections.ObjectModel;
using RadioWindows.Core;
using RadioWindows.Core.Data;

namespace RadioWindows.ViewModels;

public sealed class HelpTopicViewModel : ObservableObject
{
    private bool _isExpanded;

    public HelpTopicViewModel(HelpTopic topic, Action<HelpTopicViewModel> toggle)
    {
        Topic = topic;
        ToggleCommand = new RelayCommand(() => toggle(this));
    }

    public HelpTopic Topic { get; }

    public string Title => Topic.Title;

    public IReadOnlyList<string> Paragraphs => Topic.Paragraphs;

    public RelayCommand ToggleCommand { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value))
            {
                Raise(nameof(Sign));
            }
        }
    }

    public string Sign => IsExpanded ? "−" : "+";
}

/// <summary>In-app documentation page.</summary>
public sealed class HelpViewModel : ObservableObject
{
    private HelpTopicViewModel? _expanded;

    public HelpViewModel()
    {
        foreach (var topic in HelpGuide.Topics)
        {
            Topics.Add(new HelpTopicViewModel(topic, Toggle));
        }
    }

    public ObservableCollection<HelpTopicViewModel> Topics { get; } = [];

    public string Title => Loc.T("Help.Title");

    public string Subtitle => Loc.T("Help.Subtitle");

    public void Expand(string id)
    {
        var next = Topics.FirstOrDefault(topic => topic.Topic.Id == id);
        if (next is not null)
        {
            Toggle(next);
        }
    }

    private void Toggle(HelpTopicViewModel topic)
    {
        if (ReferenceEquals(_expanded, topic))
        {
            topic.IsExpanded = false;
            _expanded = null;
            return;
        }

        if (_expanded is not null)
        {
            _expanded.IsExpanded = false;
        }

        topic.IsExpanded = true;
        _expanded = topic;
    }
}
