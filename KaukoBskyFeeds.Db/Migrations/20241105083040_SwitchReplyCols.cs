using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KaukoBskyFeeds.Db.Migrations
{
    /// <inheritdoc />
    public partial class SwitchReplyCols : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Reply",
                table: "Posts",
                newName: "ReplyRootUri");

            migrationBuilder.AddColumn<string>(
                name: "ReplyParentUri",
                table: "Posts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReplyParentUri",
                table: "Posts");

            migrationBuilder.RenameColumn(
                name: "ReplyRootUri",
                table: "Posts",
                newName: "Reply");
        }
    }
}
