using Veda.Storage.Entities;

namespace Veda.Storage;

public class VedaDbContext(DbContextOptions<VedaDbContext> options) : DbContext(options)
{
    public DbSet<VectorChunkEntity> VectorChunks => Set<VectorChunkEntity>();
    public DbSet<PromptTemplateEntity> PromptTemplates => Set<PromptTemplateEntity>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<VectorChunkEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ContentHash).IsUnique();
            e.HasIndex(x => x.DocumentId);
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.EmbeddingBlob).IsRequired();
        });

        mb.Entity<PromptTemplateEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Name, x.Version }).IsUnique();
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Version).IsRequired().HasMaxLength(50);
            e.Property(x => x.Content).IsRequired();
        });
    }
}
