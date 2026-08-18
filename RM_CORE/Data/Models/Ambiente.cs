using System;

namespace RM_Core.Data.Models
{
    public class Ambiente
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;    
        public string Unidade { get; set; } = string.Empty; 

        public bool AutoLogin { get; set; } = true;
        public string? RmVersion { get; set; }   
    }
}
