using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veda.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Stage6_TokenUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TokenUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ModelName = table.Column<string>(type: "TEXT", nullable: false),
                    OperationType = table.Column<string>(type: "TEXT", nullable: false),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletionTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenUsages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_CreatedAtUtc",
                table: "TokenUsages",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_UserId",
                table: "TokenUsages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_UserId_CreatedAtUtc",
                table: "TokenUsages",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TokenUsages");
        }
    }
}
