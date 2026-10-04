using System.Collections.Concurrent;
using System.Text;
using System.Globalization;
using System.Threading.Channels;
using MouseHoldFix.Configuration;
using MouseHoldFix.Input;
using MouseHoldFix.Localization;

namespace MouseHoldFix.Diagnostics;

internal readonly record struct LogEntry(DateTime Timestamp, string? Message, MouseInput Input,
    RepairDecision Decision, bool IsInput, AppSettings? Settings = null,
    RepairState Before = RepairState.Idle, RepairState After = RepairState.Idle, long ObservedAtMs = 0,
    TaskCompletionSource<bool>? RecycleRequest = null);

internal sealed class DiagnosticLogger : IDisposable
{
    private readonly Channel<LogEntry> _entries = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(8192)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.Wait
    });
    private readonly ConcurrentQueue<string> _recent = new();
    private readonly Task _writer;
    private readonly Action<string> _recycleDirectory;
    private readonly object _operationGate = new();
    private bool _recycling;
    private int _configurations;
    private volatile bool _diagnostic;
    private string? _lastError;
    private long _dropped;
    private int _disposed;

    internal DiagnosticLogger(string directory, Action<string>? recycleDirectory = null)
    {
        DirectoryPath = Path.GetFullPath(Path.Combine(directory, "Logs"));
        _recycleDirectory = recycleDirectory ?? LogFolderRecycler.Recycle;
        _writer = Task.Run(WriteLoop);
    }

    internal string DirectoryPath { get; }
    internal bool DiagnosticEnabled
    {
        get => _diagnostic;
        set
        {
            lock (_operationGate)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                if (value && _recycling) throw new InvalidOperationException(Texts.Get("LogOperationBusy"));
                _diagnostic = value;
            }
        }
    }
    internal bool OperationPending { get { lock (_operationGate) return _recycling || _configurations != 0; } }
    internal string? LastError => Volatile.Read(ref _lastError);
    internal long DroppedEntries => Interlocked.Read(ref _dropped);
    internal string[] RecentLines => _recent.ToArray();

    internal IDisposable BeginConfiguration()
    {
        lock (_operationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_recycling) throw new InvalidOperationException(Texts.Get("LogOperationBusy"));
            _configurations++;
            return new ConfigurationReservation(this);
        }
    }

    private sealed class ConfigurationReservation(DiagnosticLogger logger) : IDisposable
    {
        private DiagnosticLogger? _owner = logger;
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;
            lock (owner._operationGate) owner._configurations--;
        }
    }

    internal async Task<bool> RecycleLogsAsync()
    {
        lock (_operationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (DiagnosticEnabled) throw new InvalidOperationException(Texts.Get("StopLoggingBeforeDeleting"));
            if (_recycling || _configurations != 0) throw new InvalidOperationException(Texts.Get("LogOperationBusy"));
            _recycling = true;
        }
        try
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Reserve the operation before queueing, without holding the gate while waiting or recycling.
            await _entries.Writer.WriteAsync(new(DateTime.Now, null, default, default, false,
                RecycleRequest: completion)).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        finally { lock (_operationGate) _recycling = false; }
    }

    internal void Info(string message) => Enqueue(new(DateTime.Now, message, default, default, false));

    internal void Configuration(AppSettings settings, string reason) =>
        Enqueue(new(DateTime.Now, reason, default, default, false, settings));

    internal void Input(MouseInput input, RepairDecision decision, AppSettings settings,
        RepairState before, RepairState after, long observedAtMs)
    {
        if (_diagnostic) Enqueue(new(DateTime.Now, null, input, decision, true,
            settings, before, after, observedAtMs));
    }

    private void Enqueue(LogEntry entry)
    {
        if (!_entries.Writer.TryWrite(entry)) Interlocked.Increment(ref _dropped);
    }

    private async Task WriteLoop()
    {
        StreamWriter? stream = null;
        string? currentPath = null;
        var part = 0;
        try
        {
            await foreach (var entry in _entries.Reader.ReadAllAsync())
            {
                if (entry.RecycleRequest is { } request)
                {
                    try
                    {
                        if (DiagnosticEnabled) throw new InvalidOperationException(Texts.Get("StopLoggingBeforeDeleting"));
                        var closingStream = stream;
                        stream = null;
                        currentPath = null;
                        part = 0;
                        closingStream?.Dispose();
                        var exists = Directory.Exists(DirectoryPath);
                        if (exists)
                        {
                            LogFolderRecycler.Validate(DirectoryPath);
                            _recycleDirectory(DirectoryPath);
                        }
                        _recent.Clear();
                        Volatile.Write(ref _lastError, null);
                        request.TrySetResult(exists);
                    }
                    catch (Exception ex)
                    {
                        Volatile.Write(ref _lastError, ex.Message);
                        request.TrySetException(ex);
                    }
                    continue;
                }
                var line = Format(entry);
                _recent.Enqueue(line);
                while (_recent.Count > 200) _recent.TryDequeue(out _);
                try
                {
                    Directory.CreateDirectory(DirectoryPath);
                    var day = entry.Timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    var path = Path.Combine(DirectoryPath, $"MouseHoldFix-{day}-{part:00}.log");
                    if (currentPath != path || (stream?.BaseStream.Length ?? 0) > 10 * 1024 * 1024)
                    {
                        stream?.Dispose();
                        stream = null;
                        if (currentPath == path) path = Path.Combine(DirectoryPath, $"MouseHoldFix-{day}-{++part:00}.log");
                        while (File.Exists(path) && new FileInfo(path).Length > 10 * 1024 * 1024)
                            path = Path.Combine(DirectoryPath, $"MouseHoldFix-{day}-{++part:00}.log");
                        stream = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
                        { AutoFlush = false };
                        currentPath = path;
                    }
                    await stream!.WriteLineAsync(line);
                    if (!_entries.Reader.TryPeek(out _)) await stream.FlushAsync();
                    Volatile.Write(ref _lastError, null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Volatile.Write(ref _lastError, ex.Message);
                    stream?.Dispose();
                    stream = null;
                    currentPath = null;
                    // Drain the bounded queue even when the disk is unavailable.
                }
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, ex.Message);
            _entries.Writer.TryComplete(ex);
        }
        finally
        {
            _entries.Writer.TryComplete();
            while (_entries.Reader.TryRead(out var remaining))
                remaining.RecycleRequest?.TrySetException(new ObjectDisposedException(nameof(DiagnosticLogger)));
            try { stream?.Dispose(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Volatile.Write(ref _lastError, ex.Message); }
        }
    }

    private static string Format(LogEntry entry)
    {
        var prefix = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        if (!entry.IsInput && entry.Settings is { } settings)
            return FormattableString.Invariant($"{prefix} CONFIG reason={entry.Message} enabled={settings.Enabled.ToString().ToLowerInvariant()} grace_ms={settings.ReleaseGraceMs} drag_only={settings.DragOnly.ToString().ToLowerInvariant()} diagnostics={settings.DiagnosticLogging.ToString().ToLowerInvariant()} startup={settings.StartWithWindows.ToString().ToLowerInvariant()}");
        if (!entry.IsInput) return $"{prefix} {entry.Message}";
        var name = entry.Input.Kind switch { MouseEventKind.LeftDown => "LDOWN", MouseEventKind.LeftUp => "LUP", _ => "MOVE" };
        var action = entry.Settings?.Enabled == false ? " PASS_DISABLED" :
            entry.Decision.Repaired ? " SUPPRESSED" : entry.Decision.Block ? " HOLD" : " PASS";
        var gap = entry.Decision.UpDownGapMs is { } ms ? $" gap={ms}ms" : "";
        var repair = entry.Decision.Repaired ? $" repair={entry.Decision.UpDownGapMs}ms" : "";
        var drag = entry.Decision.DragStarted ? " DRAG_START" : "";
        return FormattableString.Invariant($"{prefix} {name} x={entry.Input.X} y={entry.Input.Y}{action}{gap}{repair}{drag} state={entry.Before}->{entry.After} event_ms={entry.Input.TimestampMs} age_ms={Math.Max(0, entry.ObservedAtMs - entry.Input.TimestampMs)} grace_ms={entry.Settings?.ReleaseGraceMs}");
    }

    public void Dispose()
    {
        lock (_operationGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _entries.Writer.TryComplete();
        }
        _writer.Wait(TimeSpan.FromSeconds(3));
    }
}
