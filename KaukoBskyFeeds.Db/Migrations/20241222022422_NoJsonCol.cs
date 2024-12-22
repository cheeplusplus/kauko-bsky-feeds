using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KaukoBskyFeeds.Db.Migrations
{
    /// <inheritdoc />
    public partial class NoJsonCol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Embeds",
                table: "Posts");

            migrationBuilder.AddColumn<string>(
                name: "EmbedRecordUri",
                table: "Posts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbedType",
                table: "Posts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImageCount",
                table: "Posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmbedRecordUri",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "EmbedType",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "ImageCount",
                table: "Posts");

            migrationBuilder.AddColumn<string>(
                name: "Embeds",
                table: "Posts",
                type: "jsonb",
                nullable: true);
        }
    }
}
