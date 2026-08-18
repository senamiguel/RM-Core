using System.Collections.Generic;
using System.Threading.Tasks;

namespace RM_Core.Services.Telemetry
{
    public interface ITelemetrySink
    {
        Task SendAsync(List<TelemetryEvent> events);
    }
}
