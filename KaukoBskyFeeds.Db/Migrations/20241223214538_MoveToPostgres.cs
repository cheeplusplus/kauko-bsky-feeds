using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KaukoBskyFeeds.Db.Migrations
{
    /// <inheritdoc />
    public partial class MoveToPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PostLikes",
                columns: table => new
                {
                    LikeDid = table.Column<string>(type: "text", nullable: false),
                    LikeRkey = table.Column<string>(type: "text", nullable: false),
                    ParentDid = table.Column<string>(type: "text", nullable: false),
                    ParentRkey = table.Column<string>(type: "text", nullable: false),
                    EventTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    EventTimeUs = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostLikes", x => new { x.LikeDid, x.LikeRkey });
                }
            );

            migrationBuilder.CreateTable(
                name: "PostQuotePosts",
                columns: table => new
                {
                    QuoteDid = table.Column<string>(type: "text", nullable: false),
                    QuoteRkey = table.Column<string>(type: "text", nullable: false),
                    ParentDid = table.Column<string>(type: "text", nullable: false),
                    ParentRkey = table.Column<string>(type: "text", nullable: false),
                    EventTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    EventTimeUs = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostQuotePosts", x => new { x.QuoteDid, x.QuoteRkey });
                }
            );

            migrationBuilder.CreateTable(
                name: "PostReplies",
                columns: table => new
                {
                    ReplyDid = table.Column<string>(type: "text", nullable: false),
                    ReplyRkey = table.Column<string>(type: "text", nullable: false),
                    ParentDid = table.Column<string>(type: "text", nullable: false),
                    ParentRkey = table.Column<string>(type: "text", nullable: false),
                    EventTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    EventTimeUs = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostReplies", x => new { x.ReplyDid, x.ReplyRkey });
                }
            );

            migrationBuilder.CreateTable(
                name: "PostReposts",
                columns: table => new
                {
                    RepostDid = table.Column<string>(type: "text", nullable: false),
                    RepostRkey = table.Column<string>(type: "text", nullable: false),
                    ParentDid = table.Column<string>(type: "text", nullable: false),
                    ParentRkey = table.Column<string>(type: "text", nullable: false),
                    EventTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    EventTimeUs = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostReposts", x => new { x.RepostDid, x.RepostRkey });
                }
            );

            migrationBuilder.CreateTable(
                name: "Posts",
                columns: table => new
                {
                    Did = table.Column<string>(type: "text", nullable: false),
                    Rkey = table.Column<string>(type: "text", nullable: false),
                    EventTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    EventTimeUs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    Langs = table.Column<List<string>>(type: "text[]", nullable: true),
                    Text = table.Column<string>(
                        type: "character varying(3000)",
                        maxLength: 3000,
                        nullable: false
                    ),
                    ReplyParentUri = table.Column<string>(type: "text", nullable: true),
                    ReplyRootUri = table.Column<string>(type: "text", nullable: true),
                    EmbedType = table.Column<string>(type: "text", nullable: true),
                    ImageCount = table.Column<int>(type: "integer", nullable: false),
                    EmbedRecordUri = table.Column<string>(type: "text", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Posts", x => new { x.Did, x.Rkey });
                }
            );

            /*migrationBuilder.CreateIndex(
                name: "IX_PostLikes_EventTime",
                table: "PostLikes",
                column: "EventTime"
            );*/

            migrationBuilder.CreateIndex(
                name: "IX_PostLikes_ParentDid_ParentRkey",
                table: "PostLikes",
                columns: new[] { "ParentDid", "ParentRkey" }
            );

            /*migrationBuilder.CreateIndex(
                name: "IX_PostQuotePosts_EventTime",
                table: "PostQuotePosts",
                column: "EventTime"
            );*/

            migrationBuilder.CreateIndex(
                name: "IX_PostQuotePosts_ParentDid_ParentRkey",
                table: "PostQuotePosts",
                columns: new[] { "ParentDid", "ParentRkey" }
            );

            /*migrationBuilder.CreateIndex(
                name: "IX_PostReplies_EventTime",
                table: "PostReplies",
                column: "EventTime"
            );*/

            migrationBuilder.CreateIndex(
                name: "IX_PostReplies_ParentDid_ParentRkey",
                table: "PostReplies",
                columns: new[] { "ParentDid", "ParentRkey" }
            );

            /*migrationBuilder.CreateIndex(
                name: "IX_PostReposts_EventTime",
                table: "PostReposts",
                column: "EventTime"
            );*/

            migrationBuilder.CreateIndex(
                name: "IX_PostReposts_ParentDid_ParentRkey",
                table: "PostReposts",
                columns: new[] { "ParentDid", "ParentRkey" }
            );

            /*migrationBuilder.CreateIndex(
                name: "IX_Posts_EventTime",
                table: "Posts",
                column: "EventTime"
            );*/

            // Custom view to make it easier to pull interaction counts
            migrationBuilder.Sql(
                @"CREATE OR REPLACE VIEW ""PostsWithInteractions"" AS
    SELECT p.*,
        (SELECT COUNT(DISTINCT(pl.""LikeDid"", pl.""LikeRkey"")) FROM ""PostLikes"" AS pl WHERE pl.""ParentDid"" = p.""Did"" AND pl.""ParentRkey"" = p.""Rkey"") AS ""LikeCount"",
        (SELECT COUNT(DISTINCT(pl.""QuoteDid"", pl.""QuoteRkey"")) FROM ""PostQuotePosts"" AS pl WHERE pl.""ParentDid"" = p.""Did"" AND pl.""ParentRkey"" = p.""Rkey"") AS ""QuotePostCount"",
        (SELECT COUNT(DISTINCT(pl.""ReplyDid"", pl.""ReplyRkey"")) FROM ""PostReplies"" AS pl WHERE pl.""ParentDid"" = p.""Did"" AND pl.""ParentRkey"" = p.""Rkey"") AS ""ReplyCount"",
        (SELECT COUNT(DISTINCT(pl.""RepostDid"", pl.""RepostRkey"")) FROM ""PostReposts"" AS pl WHERE pl.""ParentDid"" = p.""Did"" AND pl.""ParentRkey"" = p.""Rkey"") AS ""RepostCount""
    FROM ""Posts"" as p;
                "
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PostLikes");

            migrationBuilder.DropTable(name: "PostQuotePosts");

            migrationBuilder.DropTable(name: "PostReplies");

            migrationBuilder.DropTable(name: "PostReposts");

            migrationBuilder.DropTable(name: "Posts");

            migrationBuilder.Sql(@"DROP VIEW IF EXISTS ""PostsWithInteractions"";");
        }
    }
}
