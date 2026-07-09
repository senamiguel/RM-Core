using System;
using System.IO;
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
            string dbFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RM_Core");
            if (!Directory.Exists(dbFolder))
            {
                Directory.CreateDirectory(dbFolder);
            }
            options.UseSqlite($"Data Source={Path.Combine(dbFolder, "rmcore.db")}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AliasModel>()
                .HasOne(a => a.Ambiente)
                .WithMany()
                .HasForeignKey(a => a.AmbienteId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AmbienteConfig>()
                .HasOne(c => c.Ambiente)
                .WithOne()
                .HasForeignKey<AmbienteConfig>(c => c.AmbienteId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
