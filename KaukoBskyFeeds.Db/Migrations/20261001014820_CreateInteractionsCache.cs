using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KaukoBskyFeeds.Db.Migrations
{
    /// <inheritdoc />
    public partial class CreateInteractionsCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedInteractions",
                columns: table => new
                {
                    FeedUri = table.Column<string>(type: "text", nullable: false),
                    UserDid = table.Column<string>(type: "text", nullable: false),
                    ItemUri = table.Column<string>(type: "text", nullable: false),
                    EventTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Interaction = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedInteractions", x => new { x.FeedUri, x.UserDid, x.ItemUri });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedInteractions");
        }
    }
}
