using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using MouseHoldFix.Configuration;
using MouseHoldFix.Diagnostics;
using MouseHoldFix.Localization;

namespace MouseHoldFix.Input;

internal sealed record HookSnapshot(RepairState State, SessionStatistics Statistics, bool Enabled, string? Error);

/// <summary>The message loop owns all engine state. Pool timers only post a wake-up message.</summary>
internal sealed class MouseHook : IDisposable
{
    private const int WorkMessage = 0x8001, DeadlineMessage = 0x8002;
    private readonly ConcurrentQueue<(Action Work, TaskCompletionSource? Completion)> _commands = new();
    private readonly object _commandGate = new();
    private readonly Thread _thread;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DiagnosticLogger _logger;
    private readonly NativeMethods.HookProc _callback;
    private readonly MouseRepairEngine _engine;
    private System.Threading.Timer? _timer;
    private MessageWindow? _window;
    private nint _hook, _windowHandle;
    private long? _scheduledDeadline;
    private bool _releaseOutstanding;
    private AppSettings _settings;
    private HookSnapshot _snapshot;
    private volatile bool _stopping;
    private int _disposed;

    internal MouseHook(AppSettings settings, DiagnosticLogger logger)
    {
        _settings = settings;
        _logger = logger;
        _engine = new(settings, NativeMethods.GetSystemMetrics(NativeMethods.SmCxDrag), NativeMethods.GetSystemMetrics(NativeMethods.SmCyDrag));
        _callback = Callback;
        _snapshot = new(RepairState.Idle, default, settings.Enabled, null);
        _thread = new Thread(Run) { IsBackground = true, Name = "MouseHoldFix input" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Task.GetAwaiter().GetResult();
    }

    internal HookSnapshot Snapshot => Volatile.Read(ref _snapshot);

    private void Run()
    {
        try
        {
            _window = new MessageWindow(this);
            _windowHandle = _window.Handle;
            _timer = new System.Threading.Timer(_ => NativeMethods.PostMessageW(_windowHandle, DeadlineMessage, 0, 0),
                null, Timeout.Infinite, Timeout.Infinite);
            _hook = NativeMethods.SetWindowsHookExW(NativeMethods.WhMouseLl, _callback, NativeMethods.GetModuleHandleW(null), 0);
            if (_hook == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), Texts.Get("HookInstallError"));
            _logger.Configuration(_settings, "startup");
            _ready.TrySetResult();
            Application.Run();
        }
        catch (Exception ex)
        {
            _ready.TrySetException(ex);
            Fault(ex);
        }
        finally
        {
            lock (_commandGate)
            {
                _stopping = true;
                while (_commands.TryDequeue(out var command))
                    command.Completion?.TrySetException(new ObjectDisposedException(nameof(MouseHook)));
            }
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            var release = _engine.FlushPending();
            if (release.InjectLeftUp || _releaseOutstanding) Execute(new(InjectLeftUp: true));
            if (_hook != 0) NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
            _timer?.Dispose();
            _window?.DestroyHandle();
            _windowHandle = 0;
        }
    }

    private nint Callback(int code, nint message, nint data)
    {
        if (code < 0 || _stopping) return NativeMethods.CallNextHookEx(_hook, code, message, data);
        try
        {
            var native = Marshal.PtrToStructure<NativeMethods.MouseHookData>(data);
            // Other software's injected input remains eligible. Only our own tagged input bypasses repair.
            if (native.ExtraInfo == NativeMethods.Marker) return NativeMethods.CallNextHookEx(_hook, code, message, data);
            var kind = (int)message switch
            {
                NativeMethods.WmLeftDown => MouseEventKind.LeftDown,
                NativeMethods.WmLeftUp => MouseEventKind.LeftUp,
                NativeMethods.WmMove => MouseEventKind.Move,
                _ => (MouseEventKind?)null
            };
            if (kind is null) return NativeMethods.CallNextHookEx(_hook, code, message, data);
            var now = Environment.TickCount64;
            // Extend the native unsigned 32-bit uptime across the 49.7-day wrap.
            var timestamp = EventTimestamp.Extend(native.Time, now);
            var input = new MouseInput(kind.Value, native.Position.X, native.Position.Y, timestamp);
            var before = _engine.State;
            var decision = _engine.Process(input);
            if (kind == MouseEventKind.LeftUp) _releaseOutstanding = decision.Block;
            else if (decision.Repaired) _releaseOutstanding = false;
            _logger.Input(input, decision, _settings, before, _engine.State, now);
            // Usually injection is performed by the timer message. The rare late Down must
            // insert Up + Down atomically before returning from this callback.
            var injectionOk = Execute(decision);
            ScheduleDeadline();
            if (decision.Block && (!decision.ReplayLeftDown || injectionOk)) return 1;
            return NativeMethods.CallNextHookEx(_hook, code, message, data);
        }
        catch (Exception ex)
        {
            Fault(ex);
            return NativeMethods.CallNextHookEx(_hook, code, message, data);
        }
    }

    private bool Execute(RepairDecision decision)
    {
        if (!decision.InjectLeftUp) return true;
        var result = InputInjector.Release(decision.ReplayLeftDown);
        _engine.RecordInjection(result.Failures, result.ReleaseSent);
        if (result.ReleaseSent) _releaseOutstanding = false;
        if (result.Failures > 0) _logger.Info($"ERROR SendInput Failure count={result.Failures} win32={result.LastError}");
        if (result.ReleaseSent && _settings.DiagnosticLogging) _logger.Info("SYNTHETIC_LUP");
        if (!result.Success)
        {
            // Input injection may be blocked by UIPI. Stop suppressing physical releases.
            _settings = _settings with { Enabled = false };
            _engine.Configure(_settings);
            _logger.Configuration(_settings, "send-input-failure");
            Publish(Texts.Get("InjectionFailure"));
        }
        return result.Success;
    }

    private void ScheduleDeadline()
    {
        var due = _engine.ReleaseDeadlineMs;
        if (due == _scheduledDeadline) return;
        _scheduledDeadline = due;
        _timer?.Change(due is { } deadline ? (int)Math.Clamp(deadline + 1 - Environment.TickCount64, 1, AppSettings.MaximumGraceMs + 1) : Timeout.Infinite,
            Timeout.Infinite);
    }

    private void Deadline()
    {
        if (_stopping) return;
        _scheduledDeadline = null;
        Execute(_engine.Expire(Environment.TickCount64));
        ScheduleDeadline();
        Publish(_settings.Enabled ? null : Snapshot.Error);
    }

    internal Task ConfigureAsync(AppSettings settings) => InvokeAsync(() =>
    {
        // Reject a conflicting logging operation before changing the repair engine.
        _logger.DiagnosticEnabled = settings.DiagnosticLogging;
        var decision = _engine.Configure(settings);
        _settings = settings;
        _logger.Configuration(_settings, "settings-change");
        Execute(decision);
        ScheduleDeadline();
        Publish(_settings.Enabled ? null : Snapshot.Error);
    });

    internal void RequestSnapshot() => Post(() =>
    {
        _engine.SetDragMetrics(NativeMethods.GetSystemMetrics(NativeMethods.SmCxDrag), NativeMethods.GetSystemMetrics(NativeMethods.SmCyDrag));
        Publish(Snapshot.Error);
    });

    private void Publish(string? error) => Volatile.Write(ref _snapshot,
        new HookSnapshot(_engine.State, _engine.Statistics, _settings.Enabled, error));

    private void Fault(Exception ex)
    {
        _settings = _settings with { Enabled = false };
        _logger.Configuration(_settings, "input-failure");
        Execute(_engine.Configure(_settings));
        _logger.Info($"ERROR Input: {ex.GetType().Name}: {ex.Message}");
        Publish(ex.Message);
    }

    private void Post(Action action, TaskCompletionSource? completion = null)
    {
        // Synchronize enqueueing with the final drain. No hook callback takes this lock.
        lock (_commandGate)
        {
            if (_stopping || _windowHandle == 0 || (completion is not null && Volatile.Read(ref _disposed) != 0))
            {
                completion?.TrySetException(new ObjectDisposedException(nameof(MouseHook)));
                return;
            }
            _commands.Enqueue((action, completion));
            if (!NativeMethods.PostMessageW(_windowHandle, WorkMessage, 0, 0))
                completion?.TrySetException(new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }

    private Task InvokeAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(action, completion);
        return completion.Task;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (!_stopping) Post(() =>
        {
            _stopping = true;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            Execute(_engine.Configure(_settings with { Enabled = false }));
            Application.ExitThread();
        });
        if (Thread.CurrentThread != _thread && !_thread.Join(TimeSpan.FromSeconds(3)))
        {
            // Best effort when a native call prevents the message thread from shutting down.
            InputInjector.Release();
            _logger.Info("ERROR Input thread did not stop within 3 seconds; emergency LeftUp sent.");
        }
        GC.KeepAlive(_callback);
    }

    private sealed class MessageWindow : NativeWindow
    {
        private readonly MouseHook _owner;
        internal MessageWindow(MouseHook owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams { Caption = "MouseHoldFix input dispatcher", Parent = new nint(-3) });
        }

        protected override void WndProc(ref Message message)
        {
            try
            {
                if (message.Msg == WorkMessage)
                {
                    while (_owner._commands.TryDequeue(out var command))
                    {
                        if (command.Completion?.Task.IsCompleted == true) continue;
                        if (_owner._stopping)
                        {
                            command.Completion?.TrySetException(new ObjectDisposedException(nameof(MouseHook)));
                            continue;
                        }
                        try { command.Work(); command.Completion?.TrySetResult(); }
                        catch (Exception ex)
                        {
                            if (command.Completion is null) throw;
                            command.Completion.TrySetException(ex);
                        }
                    }
                    return;
                }
                if (message.Msg == DeadlineMessage) { _owner.Deadline(); return; }
            }
            catch (Exception ex) { _owner.Fault(ex); }
            base.WndProc(ref message);
        }
    }
}
