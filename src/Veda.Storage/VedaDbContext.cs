using Veda.Storage.Entities;

namespace Veda.Storage;

public class VedaDbContext(DbContextOptions<VedaDbContext> options) : DbContext(options)
{
    public DbSet<VectorChunkEntity> VectorChunks => Set<VectorChunkEntity>();
    public DbSet<PromptTemplateEntity> PromptTemplates => Set<PromptTemplateEntity>();
    public DbSet<SyncedFileEntity> SyncedFiles => Set<SyncedFileEntity>();
    public DbSet<EvalQuestionEntity> EvalQuestions => Set<EvalQuestionEntity>();
    public DbSet<EvalRunEntity> EvalRuns => Set<EvalRunEntity>();

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

        mb.Entity<SyncedFileEntity>(e =>
        {
            e.HasKey(x => x.Id);
            // (ConnectorName, FilePath) is unique — one record per file per connector
            e.HasIndex(x => new { x.ConnectorName, x.FilePath }).IsUnique();
            e.Property(x => x.ConnectorName).IsRequired().HasMaxLength(100);
            e.Property(x => x.FilePath).IsRequired().HasMaxLength(2000);
            e.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
            e.Property(x => x.DocumentId).IsRequired().HasMaxLength(200);
        });

        mb.Entity<EvalQuestionEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Question).IsRequired();
            e.Property(x => x.ExpectedAnswer).IsRequired();
            e.Property(x => x.TagsJson).IsRequired().HasDefaultValue("[]");
        });

        mb.Entity<EvalRunEntity>(e =>
        {
            e.HasKey(x => x.RunId);
            e.Property(x => x.ModelName).IsRequired().HasMaxLength(200);
            e.Property(x => x.ReportJson).IsRequired();
        });
    }
}
