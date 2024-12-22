using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KaukoBskyFeeds.Db.Migrations
{
    /// <inheritdoc />
    public partial class PostInteractionsView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
            migrationBuilder.Sql(@"DROP VIEW IF EXISTS ""PostsWithInteractions"";");
        }
    }
}
