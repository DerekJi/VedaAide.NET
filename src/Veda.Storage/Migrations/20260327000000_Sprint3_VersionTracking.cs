using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veda.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Sprint3_VersionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "VectorChunks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "SupersededAtTicks",
                table: "VectorChunks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "SupersededByDocId",
                table: "VectorChunks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_VectorChunks_DocumentName_SupersededAtTicks",
                table: "VectorChunks",
                columns: new[] { "DocumentName", "SupersededAtTicks" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VectorChunks_DocumentName_SupersededAtTicks",
                table: "VectorChunks");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "VectorChunks");

            migrationBuilder.DropColumn(
                name: "SupersededAtTicks",
                table: "VectorChunks");

            migrationBuilder.DropColumn(
                name: "SupersededByDocId",
                table: "VectorChunks");
        }
    }
}
