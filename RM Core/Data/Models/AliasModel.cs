using System;

namespace RM_Core.Data.Models
{
    public class AliasModel
    {
        public int Id { get; set; }
        public int AmbienteId { get; set; }
        public Ambiente Ambiente { get; set; } = null!;
        public string Nome { get; set; } = string.Empty;         // Nome para exibição
        public string Usuario { get; set; } = string.Empty;      // RM user
        public string Senha { get; set; } = string.Empty;        // RM pass
        public string Servidor { get; set; } = string.Empty;     // host:port
        public string BaseName { get; set; } = string.Empty;     // CorporeRM
        public bool RunService { get; set; } = true;
        public bool JobServerEnabled { get; set; }
        public bool JobServerProcessPoolEnabled { get; set; }
        public bool JobServerLocalOnly { get; set; }
        public int JobServerMaxThreads { get; set; }
        public string DbType { get; set; } = "SqlServer";        // "SqlServer" | "Oracle"
        public string DbProvider { get; set; } = "SqlClient";    // "SqlClient" | "OracleClient"
        public string DbServer { get; set; } = string.Empty;
        public string DbName { get; set; } = string.Empty;
        public string Sgbd { get; set; } = string.Empty;
        public string DbUser { get; set; } = string.Empty;
        public string DbPass { get; set; } = string.Empty;
    }
}
