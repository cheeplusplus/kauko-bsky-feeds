using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KaukoBskyFeeds.Db.Migrations
{
    /// <inheritdoc />
    public partial class FixInteractionsCacheKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_FeedInteractions", table: "FeedInteractions");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "FeedInteractions",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()"
            );

            migrationBuilder.AddPrimaryKey(
                name: "PK_FeedInteractions",
                table: "FeedInteractions",
                column: "Id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeedInteractions_FeedUri_UserDid_ItemUri",
                table: "FeedInteractions",
                columns: new[] { "FeedUri", "UserDid", "ItemUri" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_FeedInteractions", table: "FeedInteractions");

            migrationBuilder.DropIndex(
                name: "IX_FeedInteractions_FeedUri_UserDid_ItemUri",
                table: "FeedInteractions"
            );

            migrationBuilder.DropColumn(name: "Id", table: "FeedInteractions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_FeedInteractions",
                table: "FeedInteractions",
                columns: new[] { "FeedUri", "UserDid", "ItemUri" }
            );
        }
    }
}
