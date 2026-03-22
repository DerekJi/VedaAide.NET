using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veda.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_SyncedFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_SyncedFiles_ConnectorName_FilePath",
                table: "SyncedFiles",
                columns: new[] { "ConnectorName", "FilePath" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncedFiles");
        }
    }
}
