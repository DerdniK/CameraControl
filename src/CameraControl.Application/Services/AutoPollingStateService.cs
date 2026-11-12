using CameraControl.Application.DTOs;
using CameraControl.Application.Interfaces;

namespace CameraControl.Application.Services;

public sealed class AutoPollingStateService : IAutoPollingStateService
{
    private readonly Lock _sync = new();
    private bool _enabled;
    private int _intervalSeconds;
    private DateTime? _lastCycleAtUtc;
    private int _totalAutomatedCycles;

    public AutoPollingStateService(bool initialEnabled = false, int initialIntervalSeconds = 15)
    {
        _enabled = initialEnabled;
        _intervalSeconds = Math.Clamp(initialIntervalSeconds, 3, 3600);
    }

    public bool Enabled
    {
        get { lock (_sync) return _enabled; }
    }

    public int IntervalSeconds
    {
        get { lock (_sync) return _intervalSeconds; }
    }

    public DateTime? LastCycleAtUtc
    {
        get { lock (_sync) return _lastCycleAtUtc; }
    }

    public int TotalAutomatedCycles
    {
        get { lock (_sync) return _totalAutomatedCycles; }
    }

    public PollingConfigurationDto GetStatus()
    {
        lock (_sync)
        {
            return new PollingConfigurationDto(_enabled, _intervalSeconds, _lastCycleAtUtc, _totalAutomatedCycles);
        }
    }

    public PollingConfigurationDto UpdateConfiguration(bool enabled, int intervalSeconds)
    {
        lock (_sync)
        {
            _enabled = enabled;
            _intervalSeconds = Math.Clamp(intervalSeconds, 3, 3600);
            return new PollingConfigurationDto(_enabled, _intervalSeconds, _lastCycleAtUtc, _totalAutomatedCycles);
        }
    }

    public void RecordCycleExecution()
    {
        lock (_sync)
        {
            _lastCycleAtUtc = DateTime.UtcNow;
            _totalAutomatedCycles++;
        }
    }
}
