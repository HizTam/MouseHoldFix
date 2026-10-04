using MouseHoldFix.Configuration;
using MouseHoldFix.Diagnostics;

namespace MouseHoldFix.Input;

public enum RepairState { Idle, Pressed, Dragging, PendingRelease }
public enum MouseEventKind { Move, LeftDown, LeftUp }
public readonly record struct MouseInput(MouseEventKind Kind, int X, int Y, long TimestampMs);
public readonly record struct RepairDecision(
    bool Block = false, bool InjectLeftUp = false, bool ReplayLeftDown = false,
    bool DragStarted = false, bool Repaired = false, long? UpDownGapMs = null);

/// <summary>Single-threaded state machine. The host owns clocks, timers and native input.</summary>
public sealed class MouseRepairEngine
{
    private AppSettings _settings;
    private int _thresholdX, _thresholdY, _downX, _downY;
    private long _downs, _ups, _drags, _candidates, _repairs, _synthetics, _failures;
    private long? _lastUp, _lastGap;

    public MouseRepairEngine(AppSettings settings, int dragWidth, int dragHeight)
    {
        _settings = settings.Normalize();
        SetDragMetrics(dragWidth, dragHeight);
    }

    public RepairState State { get; private set; }
    public long DownTimestampMs { get; private set; }
    public long PendingTimestampMs { get; private set; }
    public long? ReleaseDeadlineMs => State == RepairState.PendingRelease
        ? PendingTimestampMs + _settings.ReleaseGraceMs : null;
    public SessionStatistics Statistics => new(_downs, _ups, _drags, _candidates,
        _repairs, _synthetics, _failures, _lastGap);

    public void SetDragMetrics(int width, int height)
    {
        _thresholdX = Math.Max(1, width / 2);
        _thresholdY = Math.Max(1, height / 2);
    }

    public RepairDecision Process(MouseInput input)
    {
        long? gap = null;
        if (input.Kind == MouseEventKind.LeftDown)
        {
            _downs++;
            if (_lastUp is { } up)
            {
                gap = Math.Max(0, input.TimestampMs - up);
                _lastGap = gap;
                _lastUp = null;
            }
        }
        else if (input.Kind == MouseEventKind.LeftUp)
        {
            _ups++;
            // A duplicate Up must not change either the grace deadline or the measured interval.
            if (State != RepairState.PendingRelease) _lastUp = input.TimestampMs;
        }

        if (!_settings.Enabled) return new(UpDownGapMs: gap);

        switch (input.Kind)
        {
            case MouseEventKind.LeftDown:
                if (State == RepairState.PendingRelease)
                {
                    var elapsed = input.TimestampMs - PendingTimestampMs;
                    if (elapsed >= 0 && elapsed <= _settings.ReleaseGraceMs)
                    {
                        _repairs++;
                        State = RepairState.Dragging;
                        return new(Block: true, Repaired: true, UpDownGapMs: gap);
                    }
                    // The physical Down is blocked and replayed AFTER the outstanding Up in
                    // one SendInput batch. Passing it here could invert the release/press order.
                    BeginPress(input);
                    return new(Block: true, InjectLeftUp: true, ReplayLeftDown: true, UpDownGapMs: gap);
                }
                if (State is RepairState.Pressed or RepairState.Dragging)
                    return new(UpDownGapMs: gap); // Duplicate Down must not reset a drag.
                BeginPress(input);
                return new(UpDownGapMs: gap);

            case MouseEventKind.Move:
                if (State == RepairState.Pressed &&
                    (Math.Abs((long)input.X - _downX) >= _thresholdX ||
                     Math.Abs((long)input.Y - _downY) >= _thresholdY))
                {
                    State = RepairState.Dragging;
                    _drags++;
                    return new(DragStarted: true);
                }
                return default;

            case MouseEventKind.LeftUp:
                if (State == RepairState.PendingRelease) return new(Block: true);
                if (State == RepairState.Dragging || (State == RepairState.Pressed && !_settings.DragOnly))
                {
                    State = RepairState.PendingRelease;
                    PendingTimestampMs = input.TimestampMs;
                    _candidates++;
                    return new(Block: true);
                }
                State = RepairState.Idle;
                return default;
            default:
                throw new ArgumentOutOfRangeException(nameof(input));
        }
    }

    // Inclusive boundary: a Down at exactly grace milliseconds can still be repaired.
    public RepairDecision Expire(long nowMs)
    {
        if (ReleaseDeadlineMs is not { } deadline || nowMs <= deadline) return default;
        State = RepairState.Idle;
        return new(InjectLeftUp: true);
    }

    public RepairDecision Configure(AppSettings settings)
    {
        var normalized = settings.Normalize();
        if (normalized.Enabled == _settings.Enabled && normalized.DragOnly == _settings.DragOnly &&
            normalized.ReleaseGraceMs == _settings.ReleaseGraceMs)
        {
            _settings = normalized;
            return default;
        }
        var release = FlushPending();
        _settings = normalized;
        // If a real button is still held, preserve the drag when changing options.
        // Re-enabling after pass-through waits for the next fresh Down.
        if (!normalized.Enabled) State = RepairState.Idle;
        return release;
    }

    public RepairDecision FlushPending()
    {
        if (State != RepairState.PendingRelease) return default;
        State = RepairState.Idle;
        return new(InjectLeftUp: true);
    }

    public void RecordInjection(int failures, bool releaseSent)
    {
        _failures += failures;
        if (releaseSent) _synthetics++;
    }

    private void BeginPress(MouseInput input)
    {
        _downX = input.X;
        _downY = input.Y;
        DownTimestampMs = input.TimestampMs;
        State = RepairState.Pressed;
    }
}
