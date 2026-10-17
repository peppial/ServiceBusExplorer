using CommunityToolkit.Mvvm.ComponentModel;

namespace SbExplorer.App.ViewModels;

public enum RetrieveMode
{
    Peek,
    ReceiveAndDelete,
    ReceiveLocked,
}

/// <summary>The original's "Retrieve messages" dialog: peek or receive, top N or all, from a sequence number.</summary>
public partial class ReceiveOptionsViewModel(string title, bool canReceive, long? knownCount) : ObservableObject
{
    /// <summary>"All" is capped so a huge queue can't exhaust memory; Peek next continues from there.</summary>
    public const int AllLimit = 10_000;

    public string Title { get; } = title;
    public bool CanReceive { get; } = canReceive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPeek), nameof(IsReceiveAndDelete), nameof(IsReceiveLocked))]
    private RetrieveMode _mode = RetrieveMode.Peek;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTop))]
    private bool _isAll;

    [ObservableProperty]
    private int _count = 10;

    [ObservableProperty]
    private string _fromSequenceNumber = string.Empty;

    public bool IsPeek { get => Mode == RetrieveMode.Peek; set { if (value) Mode = RetrieveMode.Peek; } }
    public bool IsReceiveAndDelete { get => Mode == RetrieveMode.ReceiveAndDelete; set { if (value) Mode = RetrieveMode.ReceiveAndDelete; } }
    public bool IsReceiveLocked { get => Mode == RetrieveMode.ReceiveLocked; set { if (value) Mode = RetrieveMode.ReceiveLocked; } }
    public bool IsTop { get => !IsAll; set => IsAll = !value; }

    public string AllLabel => knownCount is > 0 ? $"All ({knownCount:N0})" : "All";

    public int EffectiveCount => IsAll ? (int)Math.Min(knownCount is > 0 ? knownCount.Value : AllLimit, AllLimit) : Math.Max(1, Count);

    public long? FromSequence => long.TryParse(FromSequenceNumber, out var value) ? value : null;
}
