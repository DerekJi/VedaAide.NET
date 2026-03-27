using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veda.Storage.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsensusCandidates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    AnonymizedPattern = table.Column<string>(type: "TEXT", nullable: false),
                    SupportRatio = table.Column<double>(type: "REAL", nullable: false),
                    NominatedAtTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    IsApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReviewerId = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewedAtTicks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsensusCandidates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentPermissions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<string>(type: "TEXT", nullable: false),
                    GroupId = table.Column<string>(type: "TEXT", nullable: false),
                    GrantedAtTicks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvalQuestions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Question = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedAnswer = table.Column<string>(type: "TEXT", nullable: false),
                    TagsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    CreatedAtTicks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvalQuestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvalRuns",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "TEXT", nullable: false),
                    RunAtTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ModelName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ReportJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvalRuns", x => x.RunId);
                });

            migrationBuilder.CreateTable(
                name: "PromptTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentType = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtTicks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SemanticCacheEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    EmbeddingBlob = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Answer = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtTicks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticCacheEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SharingGroups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    MembersJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtTicks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharingGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncedFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConnectorName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DocumentId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncedFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserBehaviors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    RelatedChunkId = table.Column<string>(type: "TEXT", nullable: false),
                    RelatedDocumentId = table.Column<string>(type: "TEXT", nullable: false),
                    Query = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtTicks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBehaviors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VectorChunks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentName = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentType = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    ChunkIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    EmbeddingBlob = table.Column<byte[]>(type: "BLOB", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "TEXT", nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    SupersededAtTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    SupersededByDocId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VectorChunks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsensusCandidates_IsApproved",
                table: "ConsensusCandidates",
                column: "IsApproved");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPermissions_DocumentId_GroupId",
                table: "DocumentPermissions",
                columns: new[] { "DocumentId", "GroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromptTemplates_Name_Version",
                table: "PromptTemplates",
                columns: new[] { "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharingGroups_OwnerId",
                table: "SharingGroups",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncedFiles_ConnectorName_FilePath",
                table: "SyncedFiles",
                columns: new[] { "ConnectorName", "FilePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBehaviors_UserId",
                table: "UserBehaviors",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBehaviors_UserId_RelatedChunkId",
                table: "UserBehaviors",
                columns: new[] { "UserId", "RelatedChunkId" });

            migrationBuilder.CreateIndex(
                name: "IX_VectorChunks_ContentHash",
                table: "VectorChunks",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VectorChunks_DocumentId",
                table: "VectorChunks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_VectorChunks_DocumentName_SupersededAtTicks",
                table: "VectorChunks",
                columns: new[] { "DocumentName", "SupersededAtTicks" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsensusCandidates");

            migrationBuilder.DropTable(
                name: "DocumentPermissions");

            migrationBuilder.DropTable(
                name: "EvalQuestions");

            migrationBuilder.DropTable(
                name: "EvalRuns");

            migrationBuilder.DropTable(
                name: "PromptTemplates");

            migrationBuilder.DropTable(
                name: "SemanticCacheEntries");

            migrationBuilder.DropTable(
                name: "SharingGroups");

            migrationBuilder.DropTable(
                name: "SyncedFiles");

            migrationBuilder.DropTable(
                name: "UserBehaviors");

            migrationBuilder.DropTable(
                name: "VectorChunks");
        }
    }
}
