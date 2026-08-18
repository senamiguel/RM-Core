using System;

namespace RM_Core.Data.Models
{
    public class AmbienteConfig
    {
        public int Id { get; set; }
        public int AmbienteId { get; set; }
        public Ambiente Ambiente { get; set; } = null!;

        public bool JobServer3Camadas { get; set; }
        public string DefaultDB { get; set; } = "CorporeRM";
        public bool NormalizePath { get; set; }
        public bool EnableProcessIsolation { get; set; }

        public bool EnableCompression { get; set; }
        public bool DelBroker { get; set; }
        public bool VerboseLogs { get; set; }
        public bool ApagarHost { get; set; }
    }
}
