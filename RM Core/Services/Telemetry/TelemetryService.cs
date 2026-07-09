using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace RM_Core.Services.Telemetry
{
    public class TelemetryService : IDisposable
    {
        private const int BatchSize = 10;
        private const int FlushIntervalMs = 30_000;

        private readonly List<ITelemetrySink> _sinks;
        private readonly List<TelemetryEvent> _queue = new();
        private readonly object _lock = new();
        private readonly System.Timers.Timer _flushTimer;
        private readonly string _queuePath;
        private readonly string _installId;
        private readonly string _appVersion;
        private readonly Func<int>? _clientCountProvider;
        private readonly Func<int>? _baseCountProvider;
        private bool _disposed;

        public string InstallId => _installId;

        public TelemetryService(
            string installId,
            string appVersion,
            List<ITelemetrySink> sinks,
            Func<int>? clientCountProvider = null,
            Func<int>? baseCountProvider = null)
        {
            _installId = installId;
            _appVersion = appVersion;
            _sinks = sinks;
            _clientCountProvider = clientCountProvider;
            _baseCountProvider = baseCountProvider;

            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RM_Core");
            Directory.CreateDirectory(appData);
            _queuePath = Path.Combine(appData, "telemetry_queue.json");

            LoadPersistedQueue();

            _flushTimer = new System.Timers.Timer(FlushIntervalMs);
            _flushTimer.Elapsed += (_, _) => _ = FlushAsync();
            _flushTimer.AutoReset = true;
            _flushTimer.Start();
        }

        public void Track(string eventName, Dictionary<string, object>? props = null)
        {
            if (_disposed) return;
            try
            {
                var ev = new TelemetryEvent
                {
                    Event = eventName,
                    InstallId = _installId,
                    Timestamp = DateTime.UtcNow,
                    AppVersion = _appVersion,
                    OsVersion = Environment.OSVersion.VersionString,
                    DotnetVersion = Environment.Version.ToString(),
                    Country = System.Globalization.CultureInfo.CurrentUICulture.Name,
                    ClientCount = _clientCountProvider?.Invoke() ?? 0,
                    BaseCount = _baseCountProvider?.Invoke() ?? 0,
                    Props = props
                };

                List<TelemetryEvent>? toSend = null;
                lock (_lock)
                {
                    _queue.Add(ev);
                    if (_queue.Count >= BatchSize)
                    {
                        toSend = new List<TelemetryEvent>(_queue);
                        _queue.Clear();
                    }
                }

                if (toSend != null)
                {
                    _ = SendAsync(toSend);
                }
            }
            catch
            {
            }
        }

        public async Task FlushAsync()
        {
            if (_disposed) return;
            List<TelemetryEvent>? toSend = null;
            lock (_lock)
            {
                if (_queue.Count == 0) return;
                toSend = new List<TelemetryEvent>(_queue);
                _queue.Clear();
            }

            if (toSend != null)
            {
                await SendAsync(toSend);
            }
        }

        public void FlushSync()
        {
            try
            {
                FlushAsync().GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        private async Task SendAsync(List<TelemetryEvent> events)
        {
            foreach (var sink in _sinks)
            {
                try
                {
                    await sink.SendAsync(events);
                }
                catch
                {
                }
            }
        }

        private void LoadPersistedQueue()
        {
            try
            {
                if (!File.Exists(_queuePath)) return;
                var persisted = JsonSerializer.Deserialize<List<TelemetryEvent>>(File.ReadAllText(_queuePath));
                if (persisted != null && persisted.Count > 0)
                {
                    lock (_lock)
                    {
                        _queue.AddRange(persisted);
                    }
                }
                File.Delete(_queuePath);
            }
            catch
            {
            }
        }

        private void PersistQueue()
        {
            try
            {
                List<TelemetryEvent> snapshot;
                lock (_lock)
                {
                    if (_queue.Count == 0) return;
                    snapshot = new List<TelemetryEvent>(_queue);
                }
                File.WriteAllText(_queuePath, JsonSerializer.Serialize(snapshot));
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _flushTimer.Stop();
                _flushTimer.Dispose();
                FlushSync();
            }
            catch
            {
            }
        }
    }
}
