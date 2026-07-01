using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KaukoBskyFeeds.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedAtColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "PostReposts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "PostReplies",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "PostQuotePosts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "PostLikes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CreatedAt",
                table: "Posts",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_Did_CreatedAt",
                table: "Posts",
                columns: new[] { "Did", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PostReposts_CreatedAt",
                table: "PostReposts",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_PostReplies_CreatedAt",
                table: "PostReplies",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_PostQuotePosts_CreatedAt",
                table: "PostQuotePosts",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_PostLikes_CreatedAt",
                table: "PostLikes",
                column: "CreatedAt",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Posts_CreatedAt",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_Posts_Did_CreatedAt",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_PostReposts_CreatedAt",
                table: "PostReposts");

            migrationBuilder.DropIndex(
                name: "IX_PostReplies_CreatedAt",
                table: "PostReplies");

            migrationBuilder.DropIndex(
                name: "IX_PostQuotePosts_CreatedAt",
                table: "PostQuotePosts");

            migrationBuilder.DropIndex(
                name: "IX_PostLikes_CreatedAt",
                table: "PostLikes");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "PostReposts");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "PostReplies");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "PostQuotePosts");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "PostLikes");
        }
    }
}
