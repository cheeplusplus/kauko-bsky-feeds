using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KaukoBskyFeeds.Db.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDidFromEventIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Posts_EventTime_Did",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_PostReposts_EventTime_ParentDid",
                table: "PostReposts");

            migrationBuilder.DropIndex(
                name: "IX_PostReplies_EventTime_ParentDid",
                table: "PostReplies");

            migrationBuilder.DropIndex(
                name: "IX_PostQuotePosts_EventTime_ParentDid",
                table: "PostQuotePosts");

            migrationBuilder.DropIndex(
                name: "IX_PostLikes_EventTime_ParentDid",
                table: "PostLikes");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_EventTime",
                table: "Posts",
                column: "EventTime",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_PostReposts_EventTime",
                table: "PostReposts",
                column: "EventTime",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_PostReplies_EventTime",
                table: "PostReplies",
                column: "EventTime",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_PostQuotePosts_EventTime",
                table: "PostQuotePosts",
                column: "EventTime",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_PostLikes_EventTime",
                table: "PostLikes",
                column: "EventTime",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Posts_EventTime",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_PostReposts_EventTime",
                table: "PostReposts");

            migrationBuilder.DropIndex(
                name: "IX_PostReplies_EventTime",
                table: "PostReplies");

            migrationBuilder.DropIndex(
                name: "IX_PostQuotePosts_EventTime",
                table: "PostQuotePosts");

            migrationBuilder.DropIndex(
                name: "IX_PostLikes_EventTime",
                table: "PostLikes");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_EventTime_Did",
                table: "Posts",
                columns: new[] { "EventTime", "Did" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_PostReposts_EventTime_ParentDid",
                table: "PostReposts",
                columns: new[] { "EventTime", "ParentDid" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_PostReplies_EventTime_ParentDid",
                table: "PostReplies",
                columns: new[] { "EventTime", "ParentDid" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_PostQuotePosts_EventTime_ParentDid",
                table: "PostQuotePosts",
                columns: new[] { "EventTime", "ParentDid" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_PostLikes_EventTime_ParentDid",
                table: "PostLikes",
                columns: new[] { "EventTime", "ParentDid" },
                descending: new[] { true, false });
        }
    }
}
