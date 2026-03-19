using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veda.Storage.Migrations
{
    /// <inheritdoc />
    public partial class InitialVectorSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtTicks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VectorChunks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VectorChunks_ContentHash",
                table: "VectorChunks",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VectorChunks_DocumentId",
                table: "VectorChunks",
                column: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VectorChunks");
        }
    }
}
