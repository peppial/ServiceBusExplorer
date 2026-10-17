using CommunityToolkit.Mvvm.ComponentModel;
using SbExplorer.Core.Management;

namespace SbExplorer.App.ViewModels;

/// <summary>
/// The original explorer's Days / Hours / Minutes / Seconds / Millisecs boxes for one duration. It mirrors a text
/// property on the editor (e.g. "5m" or "Never"), so validation and saving keep working on the text.
/// </summary>
public partial class DurationPartsViewModel : ObservableObject
{
    private readonly Action<string> _setText;
    private bool _loading;

    public DurationPartsViewModel(string text, Action<string> setText)
    {
        _setText = setText;
        Load(text);
    }

    [ObservableProperty]
    private int _days;

    [ObservableProperty]
    private int _hours;

    [ObservableProperty]
    private int _minutes;

    [ObservableProperty]
    private int _seconds;

    [ObservableProperty]
    private int _milliseconds;

    /// <summary>Shows a duration typed elsewhere; "Never" shows Service Bus's maximum, 10675199 days.</summary>
    public void Load(string text)
    {
        if (!DurationInput.TryParse(text, out var value))
        {
            return;
        }

        _loading = true;
        Days = value.Days;
        Hours = value.Hours;
        Minutes = value.Minutes;
        Seconds = value.Seconds;
        Milliseconds = value.Milliseconds;
        _loading = false;
    }

    public TimeSpan Value
    {
        get
        {
            try
            {
                return new TimeSpan(Days, Hours, Minutes, Seconds, Milliseconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return TimeSpan.MaxValue;
            }
        }
    }

    partial void OnDaysChanged(int value) => Push();
    partial void OnHoursChanged(int value) => Push();
    partial void OnMinutesChanged(int value) => Push();
    partial void OnSecondsChanged(int value) => Push();
    partial void OnMillisecondsChanged(int value) => Push();

    private void Push()
    {
        if (!_loading)
        {
            // A zero duration has no text form, so it's sent as "0s" and rejected by validation.
            _setText(Value <= TimeSpan.Zero ? "0s" : DurationInput.Format(Value));
        }
    }
}
