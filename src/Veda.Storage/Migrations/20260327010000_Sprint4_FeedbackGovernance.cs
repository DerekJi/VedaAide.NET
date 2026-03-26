using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veda.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Sprint4_FeedbackGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_UserBehaviors_UserId",
                table: "UserBehaviors",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBehaviors_UserId_RelatedChunkId",
                table: "UserBehaviors",
                columns: new[] { "UserId", "RelatedChunkId" });

            migrationBuilder.CreateIndex(
                name: "IX_SharingGroups_OwnerId",
                table: "SharingGroups",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPermissions_DocumentId_GroupId",
                table: "DocumentPermissions",
                columns: new[] { "DocumentId", "GroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsensusCandidates_IsApproved",
                table: "ConsensusCandidates",
                column: "IsApproved");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserBehaviors");
            migrationBuilder.DropTable(name: "SharingGroups");
            migrationBuilder.DropTable(name: "DocumentPermissions");
            migrationBuilder.DropTable(name: "ConsensusCandidates");
        }
    }
}
