using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class FeedbackToVotesAndComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "article_comments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ArticleId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_article_comments_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_article_comments_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_votes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ArticleId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    IsHelpful = table.Column<bool>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_article_votes_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_article_votes_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Migrate existing feedback data to new tables (take latest vote per user+article)
            migrationBuilder.Sql(@"
                INSERT INTO article_votes (Id, ArticleId, UserId, IsHelpful, Reason, CreatedAt, UpdatedAt)
                SELECT Id, ArticleId, UserId, CASE WHEN Helpful = 1 THEN 1 ELSE 0 END, NULL, CreatedAt, CreatedAt
                FROM article_feedback f1
                WHERE Helpful IS NOT NULL AND UserId IS NOT NULL
                  AND CreatedAt = (SELECT MAX(f2.CreatedAt) FROM article_feedback f2
                                   WHERE f2.ArticleId = f1.ArticleId AND f2.UserId = f1.UserId AND f2.Helpful IS NOT NULL);
            ");

            migrationBuilder.Sql(@"
                INSERT INTO article_comments (Id, ArticleId, UserId, Comment, CreatedAt)
                SELECT hex(randomblob(10)) || substr(Id, 1, 1), ArticleId, UserId, Comment, CreatedAt
                FROM article_feedback
                WHERE Comment IS NOT NULL AND Comment != '' AND UserId IS NOT NULL;
            ");

            migrationBuilder.DropTable(
                name: "article_feedback");

            migrationBuilder.CreateIndex(
                name: "IX_article_comments_ArticleId",
                table: "article_comments",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_article_comments_UserId",
                table: "article_comments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_article_votes_ArticleId_UserId",
                table: "article_votes",
                columns: new[] { "ArticleId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_article_votes_UserId",
                table: "article_votes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "article_comments");

            migrationBuilder.DropTable(
                name: "article_votes");

            migrationBuilder.CreateTable(
                name: "article_feedback",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ArticleId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    Comment = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Helpful = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_feedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_article_feedback_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_article_feedback_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_article_feedback_ArticleId",
                table: "article_feedback",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_article_feedback_UserId",
                table: "article_feedback",
                column: "UserId");
        }
    }
}
