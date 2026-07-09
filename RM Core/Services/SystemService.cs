using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RM_Core.Services
{
    /// <summary>
    /// Provides system-level operations: Dual Host installation, Windows Firewall
    /// rule management, Registry read/write, and Custom DLL validation.
    /// </summary>
    public class SystemService
    {
        // ---------------------------------------------------------------
        // 1. Dual Host Installation
        // ---------------------------------------------------------------

        /// <summary>
        /// Copies RM.Host.exe as RM.Host1.exe (if not yet present) and creates
        /// RM.Host1.exe.config with Port, HttpPort and ApiPort each incremented by 1.
        /// </summary>
        /// <param name="binDir">Directory that contains RM.Host.exe.</param>
        /// <returns>A tuple with success flag and a human-readable message.</returns>
        public (bool Success, string Message) InstallDualHost(string binDir)
        {
            try
            {
                string hostExe    = Path.Combine(binDir, "RM.Host.exe");
                string host1Exe   = Path.Combine(binDir, "RM.Host1.exe");
                string hostConfig  = Path.Combine(binDir, "RM.Host.exe.config");
                string host1Config = Path.Combine(binDir, "RM.Host1.exe.config");

                if (!File.Exists(hostExe))
                    return (false, $"RM.Host.exe não encontrado em: {binDir}");

                // Copy binary only if the copy does not yet exist
                if (!File.Exists(host1Exe))
                {
                    File.Copy(hostExe, host1Exe);
                }

                // Adjust ports in the config
                if (File.Exists(hostConfig))
                {
                    var configXml = XDocument.Load(hostConfig);

                    foreach (var add in configXml.Descendants("add"))
                    {
                        var key   = add.Attribute("key")?.Value;
                        var value = add.Attribute("value")?.Value;

                        if (key == "Port" && int.TryParse(value, out int port))
                            add.SetAttributeValue("value", (port + 1).ToString());
                        else if (key == "HttpPort" && int.TryParse(value, out int httpPort))
                            add.SetAttributeValue("value", (httpPort + 1).ToString());
                        else if (key == "ApiPort" && int.TryParse(value, out int apiPort))
                            add.SetAttributeValue("value", (apiPort + 1).ToString());
                    }

                    configXml.Save(host1Config);
                    return (true, "Dual Host instalado com sucesso: RM.Host1.exe e RM.Host1.exe.config criados com portas incrementadas.");
                }

                return (true, "RM.Host1.exe copiado. Arquivo .config não encontrado — configuração não gerada.");
            }
            catch (Exception ex)
            {
                return (false, $"Erro ao instalar Dual Host: {ex.Message}");
            }
        }



        // ---------------------------------------------------------------
        // 4. Custom DLL Validation
        // ---------------------------------------------------------------

        /// <summary>
        /// Scans <paramref name="customPath"/> for *.dll files and identifies those
        /// that do not begin with "RM.Cst." or "RM." (case-insensitive).
        /// </summary>
        /// <param name="customPath">Path to the Custom DLL directory.</param>
        /// <returns>
        /// A tuple containing:
        /// <list type="bullet">
        ///   <item><description><b>Total</b> — total number of DLLs found.</description></item>
        ///   <item><description><b>Invalidas</b> — count of DLLs without the required prefix.</description></item>
        ///   <item><description><b>SemPrefixo</b> — list of file names that are invalid.</description></item>
        /// </list>
        /// </returns>
        public (int Total, int Invalidas, List<string> SemPrefixo) ValidarCustomDLLs(string customPath)
        {
            var semPrefixo = new List<string>();

            if (!Directory.Exists(customPath))
                return (0, 0, semPrefixo);

            var dlls = Directory.GetFiles(customPath, "*.dll");

            foreach (var dll in dlls)
            {
                string nome = Path.GetFileName(dll);
                bool isValid = nome.StartsWith("RM.Cst.", StringComparison.OrdinalIgnoreCase)
                            || nome.StartsWith("RM.",     StringComparison.OrdinalIgnoreCase);

                if (!isValid)
                    semPrefixo.Add(nome);
            }

            return (dlls.Length, semPrefixo.Count, semPrefixo);
        }
    }
}
