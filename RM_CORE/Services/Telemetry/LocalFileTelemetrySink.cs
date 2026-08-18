using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace RM_Core.Services.Telemetry
{
    public class LocalFileTelemetrySink : ITelemetrySink
    {
        private readonly string _logPath;
        private readonly object _lock = new();

        public LocalFileTelemetrySink()
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RM_Core");
            Directory.CreateDirectory(appData);
            _logPath = Path.Combine(appData, "telemetry.log");
        }

        public Task SendAsync(List<TelemetryEvent> events)
        {
            return Task.Run(() =>
            {
                try
                {
                    lock (_lock)
                    {
                        using var writer = new StreamWriter(_logPath, append: true);
                        foreach (var ev in events)
                        {
                            string line = JsonSerializer.Serialize(ev, new JsonSerializerOptions { WriteIndented = false });
                            writer.WriteLine(line);
                        }
                    }
                }
                catch
                {
                }
            });
        }
    }
}
