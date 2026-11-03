using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TrafficSign_Detector;

public sealed class NeuronItem : INotifyPropertyChanged
{
    private bool _isActive = true;
    private double? _score;

    public NeuronItem(string name, SignLayer layer)
    {
        Name = name;
        Layer = layer;
    }

    public string Name { get; }

    public SignLayer Layer { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            OnPropertyChanged();
            ActivityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double? Score
    {
        get => _score;
        set
        {
            _score = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScoreText));
        }
    }

    public string ScoreText => _score is null ? string.Empty : _score.Value.ToString("P1");

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? ActivityChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
