using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Interfaces;
using ValorantAfkBot.Core.Models;

namespace ValorantAfkBot.App.Services;

public sealed class ObservableLogService : IAppLogger
{
    private readonly string _logFilePath;
    private readonly object _fileLock = new();
    private readonly object _entriesLock = new();
    private readonly List<LogEntry> _structuredEntries = [];
    private readonly List<string> _entries = [];
    private readonly SynchronizationContext? _synchronizationContext;
    private int _maxEntries = 500;
    private bool _persistLogsToDisk = true;

    public ObservableLogService(string logFilePath, SynchronizationContext? synchronizationContext = null)
    {
        _logFilePath = logFilePath;
        _synchronizationContext = synchronizationContext ?? SynchronizationContext.Current;
    }

    public event Action<LogEntry>? EntryLogged;

    public IReadOnlyList<string> GetSnapshot()
    {
        lock (_entriesLock)
        {
            return _entries.ToArray();
        }
    }

    public IReadOnlyList<LogEntry> GetEntriesSnapshot()
    {
        lock (_entriesLock)
        {
            return _structuredEntries.ToArray();
        }
    }

    public void Configure(int maxEntries, bool persistLogsToDisk)
    {
        _maxEntries = Math.Clamp(maxEntries, 100, 5000);
        _persistLogsToDisk = persistLogsToDisk;
    }

    public void Log(LogSeverity severity, string message)
    {
        LogEntry entry = new(DateTimeOffset.Now, severity, message);
        string formatted = $"[{entry.Timestamp:HH:mm:ss}] [{severity}] {message}";

        void AppendEntry()
        {
            lock (_entriesLock)
            {
                _structuredEntries.Add(entry);
                _entries.Add(formatted);
                while (_structuredEntries.Count > _maxEntries)
                {
                    _structuredEntries.RemoveAt(0);
                    _entries.RemoveAt(0);
                }
            }
        }

        if (_synchronizationContext is not null)
        {
            _synchronizationContext.Post(_ => AppendEntry(), null);
        }
        else
        {
            AppendEntry();
        }

        if (_persistLogsToDisk)
        {
            string? directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (_fileLock)
            {
                File.AppendAllLines(_logFilePath, [formatted]);
            }
        }

        EntryLogged?.Invoke(entry);
    }

    public void Clear()
    {
        lock (_entriesLock)
        {
            _structuredEntries.Clear();
            _entries.Clear();
        }

        if (!_persistLogsToDisk)
        {
            return;
        }

        string? directory = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (_fileLock)
        {
            File.WriteAllText(_logFilePath, string.Empty);
        }
    }
}
