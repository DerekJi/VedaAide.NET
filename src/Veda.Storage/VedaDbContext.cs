using Veda.Storage.Entities;

namespace Veda.Storage;

public class VedaDbContext(DbContextOptions<VedaDbContext> options) : DbContext(options)
{
    public DbSet<VectorChunkEntity> VectorChunks => Set<VectorChunkEntity>();
    public DbSet<PromptTemplateEntity> PromptTemplates => Set<PromptTemplateEntity>();
    public DbSet<SyncedFileEntity> SyncedFiles => Set<SyncedFileEntity>();
    public DbSet<EvalQuestionEntity> EvalQuestions => Set<EvalQuestionEntity>();
    public DbSet<EvalRunEntity> EvalRuns => Set<EvalRunEntity>();
    public DbSet<SemanticCacheEntity> SemanticCacheEntries => Set<SemanticCacheEntity>();
    public DbSet<UserBehaviorEntity> UserBehaviors => Set<UserBehaviorEntity>();
    public DbSet<SharingGroupEntity> SharingGroups => Set<SharingGroupEntity>();
    public DbSet<DocumentPermissionEntity> DocumentPermissions => Set<DocumentPermissionEntity>();
    public DbSet<ConsensusCandidateEntity> ConsensusCandidates => Set<ConsensusCandidateEntity>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<VectorChunkEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ContentHash).IsUnique();
            e.HasIndex(x => x.DocumentId);
            e.HasIndex(x => new { x.DocumentName, x.SupersededAtTicks });
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

        mb.Entity<SemanticCacheEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EmbeddingBlob).IsRequired();
            e.Property(x => x.Answer).IsRequired();
        });

        mb.Entity<UserBehaviorEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.RelatedChunkId });
        });

        mb.Entity<SharingGroupEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OwnerId);
        });

        mb.Entity<DocumentPermissionEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DocumentId, x.GroupId }).IsUnique();
        });

        mb.Entity<ConsensusCandidateEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IsApproved);
        });
    }
}
