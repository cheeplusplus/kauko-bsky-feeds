using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KaukoBskyFeeds.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddInfoTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PostLikes",
                columns: table => new
                {
                    LikeDid = table.Column<string>(type: "TEXT", nullable: false),
                    LikeRkey = table.Column<string>(type: "TEXT", nullable: false),
                    ParentDid = table.Column<string>(type: "TEXT", nullable: false),
                    ParentRkey = table.Column<string>(type: "TEXT", nullable: false),
                    EventTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventTimeUs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostLikes", x => new { x.LikeDid, x.LikeRkey });
                });

            migrationBuilder.CreateTable(
                name: "PostQuotePosts",
                columns: table => new
                {
                    QuoteDid = table.Column<string>(type: "TEXT", nullable: false),
                    QuoteRkey = table.Column<string>(type: "TEXT", nullable: false),
                    ParentDid = table.Column<string>(type: "TEXT", nullable: false),
                    ParentRkey = table.Column<string>(type: "TEXT", nullable: false),
                    EventTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventTimeUs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostQuotePosts", x => new { x.QuoteDid, x.QuoteRkey });
                });

            migrationBuilder.CreateTable(
                name: "PostReplies",
                columns: table => new
                {
                    ReplyDid = table.Column<string>(type: "TEXT", nullable: false),
                    ReplyRkey = table.Column<string>(type: "TEXT", nullable: false),
                    ParentDid = table.Column<string>(type: "TEXT", nullable: false),
                    ParentRkey = table.Column<string>(type: "TEXT", nullable: false),
                    EventTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventTimeUs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostReplies", x => new { x.ReplyDid, x.ReplyRkey });
                });

            migrationBuilder.CreateTable(
                name: "PostReposts",
                columns: table => new
                {
                    RepostDid = table.Column<string>(type: "TEXT", nullable: false),
                    RepostRkey = table.Column<string>(type: "TEXT", nullable: false),
                    ParentDid = table.Column<string>(type: "TEXT", nullable: false),
                    ParentRkey = table.Column<string>(type: "TEXT", nullable: false),
                    EventTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventTimeUs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostReposts", x => new { x.RepostDid, x.RepostRkey });
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostLikes_EventTime",
                table: "PostLikes",
                column: "EventTime");

            migrationBuilder.CreateIndex(
                name: "IX_PostLikes_ParentDid_ParentRkey",
                table: "PostLikes",
                columns: new[] { "ParentDid", "ParentRkey" });

            migrationBuilder.CreateIndex(
                name: "IX_PostQuotePosts_EventTime",
                table: "PostQuotePosts",
                column: "EventTime");

            migrationBuilder.CreateIndex(
                name: "IX_PostQuotePosts_ParentDid_ParentRkey",
                table: "PostQuotePosts",
                columns: new[] { "ParentDid", "ParentRkey" });

            migrationBuilder.CreateIndex(
                name: "IX_PostReplies_EventTime",
                table: "PostReplies",
                column: "EventTime");

            migrationBuilder.CreateIndex(
                name: "IX_PostReplies_ParentDid_ParentRkey",
                table: "PostReplies",
                columns: new[] { "ParentDid", "ParentRkey" });

            migrationBuilder.CreateIndex(
                name: "IX_PostReposts_EventTime",
                table: "PostReposts",
                column: "EventTime");

            migrationBuilder.CreateIndex(
                name: "IX_PostReposts_ParentDid_ParentRkey",
                table: "PostReposts",
                columns: new[] { "ParentDid", "ParentRkey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PostLikes");

            migrationBuilder.DropTable(
                name: "PostQuotePosts");

            migrationBuilder.DropTable(
                name: "PostReplies");

            migrationBuilder.DropTable(
                name: "PostReposts");
        }
    }
}
