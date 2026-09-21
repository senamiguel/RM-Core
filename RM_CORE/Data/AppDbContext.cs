using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using RM_Core.Data.Models;

namespace RM_Core.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Ambiente> Ambientes => Set<Ambiente>();
        public DbSet<AliasModel> Aliases => Set<AliasModel>();
        public DbSet<AmbienteConfig> AmbienteConfigs => Set<AmbienteConfig>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            string? customDir = Environment.GetEnvironmentVariable("RMCORE_DATA_DIR");
            string dbFolder = !string.IsNullOrEmpty(customDir)
                ? customDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RM_Core");

            if (!Directory.Exists(dbFolder))
            {
                Directory.CreateDirectory(dbFolder);
            }
            options.UseSqlite($"Data Source={Path.Combine(dbFolder, "rmcore.db")};Default Timeout=5;Pooling=True");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AliasModel>()
                .HasOne(a => a.Ambiente)
                .WithMany()
                .HasForeignKey(a => a.AmbienteId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AmbienteConfig>()
                .HasOne(c => c.Ambiente)
                .WithOne()
                .HasForeignKey<AmbienteConfig>(c => c.AmbienteId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
        }

        public static void EnsureDatabaseMigrated()
        {
            try
            {
                using var db = new AppDbContext();
                db.Database.EnsureCreated();

                var conn = db.Database.GetDbConnection();
                bool wasClosed = conn.State != System.Data.ConnectionState.Open;
                if (wasClosed) conn.Open();

                try
                {
                    // 1. Garantir que as tabelas existem
                    ExecuteNonQuery(conn, @"
                        CREATE TABLE IF NOT EXISTS ""Ambientes"" (
                            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Ambientes"" PRIMARY KEY AUTOINCREMENT,
                            ""Nome"" TEXT NOT NULL DEFAULT '',
                            ""FullName"" TEXT NOT NULL DEFAULT '',
                            ""Unidade"" TEXT NOT NULL DEFAULT '',
                            ""AutoLogin"" INTEGER NOT NULL DEFAULT 1,
                            ""RmVersion"" TEXT NULL
                        );");

                    ExecuteNonQuery(conn, @"
                        CREATE TABLE IF NOT EXISTS ""AmbienteConfigs"" (
                            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_AmbienteConfigs"" PRIMARY KEY AUTOINCREMENT,
                            ""AmbienteId"" INTEGER NOT NULL DEFAULT 0,
                            ""JobServer3Camadas"" INTEGER NOT NULL DEFAULT 0,
                            ""DefaultDB"" TEXT NOT NULL DEFAULT 'CorporeRM',
                            ""NormalizePath"" INTEGER NOT NULL DEFAULT 0,
                            ""EnableProcessIsolation"" INTEGER NOT NULL DEFAULT 0,
                            ""EnableCompression"" INTEGER NOT NULL DEFAULT 0,
                            ""DelBroker"" INTEGER NOT NULL DEFAULT 0,
                            ""VerboseLogs"" INTEGER NOT NULL DEFAULT 0,
                            ""ApagarHost"" INTEGER NOT NULL DEFAULT 0,
                            CONSTRAINT ""FK_AmbienteConfigs_Ambientes_AmbienteId"" FOREIGN KEY (""AmbienteId"") REFERENCES ""Ambientes"" (""Id"") ON DELETE CASCADE
                        );");

                    ExecuteNonQuery(conn, @"
                        CREATE TABLE IF NOT EXISTS ""Aliases"" (
                            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Aliases"" PRIMARY KEY AUTOINCREMENT,
                            ""AmbienteId"" INTEGER NOT NULL DEFAULT 0,
                            ""Nome"" TEXT NOT NULL DEFAULT '',
                            ""Usuario"" TEXT NOT NULL DEFAULT '',
                            ""Senha"" TEXT NOT NULL DEFAULT '',
                            ""Servidor"" TEXT NOT NULL DEFAULT '',
                            ""BaseName"" TEXT NOT NULL DEFAULT '',
                            ""RunService"" INTEGER NOT NULL DEFAULT 1,
                            ""JobServerEnabled"" INTEGER NOT NULL DEFAULT 0,
                            ""JobServerProcessPoolEnabled"" INTEGER NOT NULL DEFAULT 0,
                            ""JobServerLocalOnly"" INTEGER NOT NULL DEFAULT 0,
                            ""JobServerMaxThreads"" INTEGER NOT NULL DEFAULT 0,
                            ""DbType"" TEXT NOT NULL DEFAULT 'SqlServer',
                            ""DbProvider"" TEXT NOT NULL DEFAULT 'SqlClient',
                            ""DbServer"" TEXT NOT NULL DEFAULT '',
                            ""DbName"" TEXT NOT NULL DEFAULT '',
                            ""Sgbd"" TEXT NOT NULL DEFAULT '',
                            ""DbUser"" TEXT NOT NULL DEFAULT '',
                            ""DbPass"" TEXT NOT NULL DEFAULT '',
                            CONSTRAINT ""FK_Aliases_Ambientes_AmbienteId"" FOREIGN KEY (""AmbienteId"") REFERENCES ""Ambientes"" (""Id"") ON DELETE CASCADE
                        );");

                    // 2. Garantir índices necessários
                    ExecuteNonQuery(conn, @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AmbienteConfigs_AmbienteId"" ON ""AmbienteConfigs"" (""AmbienteId"");");
                    ExecuteNonQuery(conn, @"CREATE INDEX IF NOT EXISTS ""IX_Aliases_AmbienteId"" ON ""Aliases"" (""AmbienteId"");");

                    // 3. Migrar colunas que possam faltar em bancos legados
                    MigrateColumns(conn, "Ambientes", new Dictionary<string, string>
                    {
                        { "Nome", "TEXT NOT NULL DEFAULT ''" },
                        { "FullName", "TEXT NOT NULL DEFAULT ''" },
                        { "Unidade", "TEXT NOT NULL DEFAULT ''" },
                        { "AutoLogin", "INTEGER NOT NULL DEFAULT 1" },
                        { "RmVersion", "TEXT NULL" }
                    });

                    MigrateColumns(conn, "AmbienteConfigs", new Dictionary<string, string>
                    {
                        { "AmbienteId", "INTEGER NOT NULL DEFAULT 0" },
                        { "JobServer3Camadas", "INTEGER NOT NULL DEFAULT 0" },
                        { "DefaultDB", "TEXT NOT NULL DEFAULT 'CorporeRM'" },
                        { "NormalizePath", "INTEGER NOT NULL DEFAULT 0" },
                        { "EnableProcessIsolation", "INTEGER NOT NULL DEFAULT 0" },
                        { "EnableCompression", "INTEGER NOT NULL DEFAULT 0" },
                        { "DelBroker", "INTEGER NOT NULL DEFAULT 0" },
                        { "VerboseLogs", "INTEGER NOT NULL DEFAULT 0" },
                        { "ApagarHost", "INTEGER NOT NULL DEFAULT 0" }
                    });

                    MigrateColumns(conn, "Aliases", new Dictionary<string, string>
                    {
                        { "AmbienteId", "INTEGER NOT NULL DEFAULT 0" },
                        { "Nome", "TEXT NOT NULL DEFAULT ''" },
                        { "Usuario", "TEXT NOT NULL DEFAULT ''" },
                        { "Senha", "TEXT NOT NULL DEFAULT ''" },
                        { "Servidor", "TEXT NOT NULL DEFAULT ''" },
                        { "BaseName", "TEXT NOT NULL DEFAULT ''" },
                        { "RunService", "INTEGER NOT NULL DEFAULT 1" },
                        { "JobServerEnabled", "INTEGER NOT NULL DEFAULT 0" },
                        { "JobServerProcessPoolEnabled", "INTEGER NOT NULL DEFAULT 0" },
                        { "JobServerLocalOnly", "INTEGER NOT NULL DEFAULT 0" },
                        { "JobServerMaxThreads", "INTEGER NOT NULL DEFAULT 0" },
                        { "DbType", "TEXT NOT NULL DEFAULT 'SqlServer'" },
                        { "DbProvider", "TEXT NOT NULL DEFAULT 'SqlClient'" },
                        { "DbServer", "TEXT NOT NULL DEFAULT ''" },
                        { "DbName", "TEXT NOT NULL DEFAULT ''" },
                        { "Sgbd", "TEXT NOT NULL DEFAULT ''" },
                        { "DbUser", "TEXT NOT NULL DEFAULT ''" },
                        { "DbPass", "TEXT NOT NULL DEFAULT ''" }
                    });

                    // 4. Remover colunas órfãs/legadas que não existem mais nos modelos (ex: ControlaIIS, Host, Port)
                    // evitando 'SQLite Error 19: NOT NULL constraint failed' em campos removidos
                    DropOrphanColumns(conn, "Ambientes", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Id", "Nome", "FullName", "Unidade", "AutoLogin", "RmVersion"
                    });

                    DropOrphanColumns(conn, "AmbienteConfigs", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Id", "AmbienteId", "JobServer3Camadas", "DefaultDB", "NormalizePath",
                        "EnableProcessIsolation", "EnableCompression", "DelBroker", "VerboseLogs", "ApagarHost"
                    });

                    DropOrphanColumns(conn, "Aliases", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Id", "AmbienteId", "Nome", "Usuario", "Senha", "Servidor", "BaseName",
                        "RunService", "JobServerEnabled", "JobServerProcessPoolEnabled", "JobServerLocalOnly",
                        "JobServerMaxThreads", "DbType", "DbProvider", "DbServer", "DbName", "Sgbd", "DbUser", "DbPass"
                    });
                }
                finally
                {
                    if (wasClosed) conn.Close();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppDbContext] Erro na migração do banco SQLite: {ex.Message}");
            }
        }

        private static void DropOrphanColumns(DbConnection conn, string tableName, HashSet<string> validColumns)
        {
            var orphanCols = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string colName = reader["name"].ToString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(colName) && !validColumns.Contains(colName))
                    {
                        orphanCols.Add(colName);
                    }
                }
            }

            if (orphanCols.Count == 0) return;

            bool anyFailed = false;
            foreach (var orphan in orphanCols)
            {
                try
                {
                    using var dropCmd = conn.CreateCommand();
                    dropCmd.CommandText = $"ALTER TABLE \"{tableName}\" DROP COLUMN \"{orphan}\";";
                    dropCmd.ExecuteNonQuery();
                    System.Diagnostics.Debug.WriteLine($"[AppDbContext] Coluna legada '{orphan}' removida com sucesso de '{tableName}'.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AppDbContext] Falha ao dropar '{orphan}' de '{tableName}': {ex.Message}");
                    anyFailed = true;
                    break;
                }
            }

            if (anyFailed)
            {
                RebuildTable(conn, tableName, validColumns);
            }
        }

        private static void RebuildTable(DbConnection conn, string tableName, HashSet<string> validColumns)
        {
            var existingCols = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string colName = reader["name"].ToString() ?? string.Empty;
                    if (validColumns.Contains(colName))
                    {
                        existingCols.Add(colName);
                    }
                }
            }

            string tempTable = $"{tableName}_migration_temp";
            string colsJoined = string.Join(", ", existingCols.Select(c => $"\"{c}\""));

            ExecuteNonQuery(conn, "PRAGMA foreign_keys = OFF;");
            try
            {
                ExecuteNonQuery(conn, $"ALTER TABLE \"{tableName}\" RENAME TO \"{tempTable}\";");

                if (tableName.Equals("Ambientes", StringComparison.OrdinalIgnoreCase))
                {
                    ExecuteNonQuery(conn, @"
                        CREATE TABLE ""Ambientes"" (
                            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Ambientes"" PRIMARY KEY AUTOINCREMENT,
                            ""Nome"" TEXT NOT NULL DEFAULT '',
                            ""FullName"" TEXT NOT NULL DEFAULT '',
                            ""Unidade"" TEXT NOT NULL DEFAULT '',
                            ""AutoLogin"" INTEGER NOT NULL DEFAULT 1,
                            ""RmVersion"" TEXT NULL
                        );");
                }
                else if (tableName.Equals("AmbienteConfigs", StringComparison.OrdinalIgnoreCase))
                {
                    ExecuteNonQuery(conn, @"
                        CREATE TABLE ""AmbienteConfigs"" (
                            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_AmbienteConfigs"" PRIMARY KEY AUTOINCREMENT,
                            ""AmbienteId"" INTEGER NOT NULL DEFAULT 0,
                            ""JobServer3Camadas"" INTEGER NOT NULL DEFAULT 0,
                            ""DefaultDB"" TEXT NOT NULL DEFAULT 'CorporeRM',
                            ""NormalizePath"" INTEGER NOT NULL DEFAULT 0,
                            ""EnableProcessIsolation"" INTEGER NOT NULL DEFAULT 0,
                            ""EnableCompression"" INTEGER NOT NULL DEFAULT 0,
                            ""DelBroker"" INTEGER NOT NULL DEFAULT 0,
                            ""VerboseLogs"" INTEGER NOT NULL DEFAULT 0,
                            ""ApagarHost"" INTEGER NOT NULL DEFAULT 0,
                            CONSTRAINT ""FK_AmbienteConfigs_Ambientes_AmbienteId"" FOREIGN KEY (""AmbienteId"") REFERENCES ""Ambientes"" (""Id"") ON DELETE CASCADE
                        );");
                    ExecuteNonQuery(conn, @"CREATE UNIQUE INDEX ""IX_AmbienteConfigs_AmbienteId"" ON ""AmbienteConfigs"" (""AmbienteId"");");
                }
                else if (tableName.Equals("Aliases", StringComparison.OrdinalIgnoreCase))
                {
                    ExecuteNonQuery(conn, @"
                        CREATE TABLE ""Aliases"" (
                            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Aliases"" PRIMARY KEY AUTOINCREMENT,
                            ""AmbienteId"" INTEGER NOT NULL DEFAULT 0,
                            ""Nome"" TEXT NOT NULL DEFAULT '',
                            ""Usuario"" TEXT NOT NULL DEFAULT '',
                            ""Senha"" TEXT NOT NULL DEFAULT '',
                            ""Servidor"" TEXT NOT NULL DEFAULT '',
                            ""BaseName"" TEXT NOT NULL DEFAULT '',
                            ""RunService"" INTEGER NOT NULL DEFAULT 1,
                            ""JobServerEnabled"" INTEGER NOT NULL DEFAULT 0,
                            ""JobServerProcessPoolEnabled"" INTEGER NOT NULL DEFAULT 0,
                            ""JobServerLocalOnly"" INTEGER NOT NULL DEFAULT 0,
                            ""JobServerMaxThreads"" INTEGER NOT NULL DEFAULT 0,
                            ""DbType"" TEXT NOT NULL DEFAULT 'SqlServer',
                            ""DbProvider"" TEXT NOT NULL DEFAULT 'SqlClient',
                            ""DbServer"" TEXT NOT NULL DEFAULT '',
                            ""DbName"" TEXT NOT NULL DEFAULT '',
                            ""Sgbd"" TEXT NOT NULL DEFAULT '',
                            ""DbUser"" TEXT NOT NULL DEFAULT '',
                            ""DbPass"" TEXT NOT NULL DEFAULT '',
                            CONSTRAINT ""FK_Aliases_Ambientes_AmbienteId"" FOREIGN KEY (""AmbienteId"") REFERENCES ""Ambientes"" (""Id"") ON DELETE CASCADE
                        );");
                    ExecuteNonQuery(conn, @"CREATE INDEX ""IX_Aliases_AmbienteId"" ON ""Aliases"" (""AmbienteId"");");
                }

                if (existingCols.Count > 0)
                {
                    ExecuteNonQuery(conn, $"INSERT INTO \"{tableName}\" ({colsJoined}) SELECT {colsJoined} FROM \"{tempTable}\";");
                }

                ExecuteNonQuery(conn, $"DROP TABLE \"{tempTable}\";");
            }
            finally
            {
                ExecuteNonQuery(conn, "PRAGMA foreign_keys = ON;");
            }
        }

        private static void MigrateColumns(DbConnection conn, string tableName, Dictionary<string, string> columns)
        {
            var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    existingCols.Add(reader["name"].ToString() ?? string.Empty);
                }
            }

            foreach (var kvp in columns)
            {
                if (!existingCols.Contains(kvp.Key))
                {
                    try
                    {
                        using var alterCmd = conn.CreateCommand();
                        alterCmd.CommandText = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{kvp.Key}\" {kvp.Value};";
                        alterCmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AppDbContext] Falha ao adicionar coluna {kvp.Key} em {tableName}: {ex.Message}");
                    }
                }
            }
        }

        private static void ExecuteNonQuery(DbConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
