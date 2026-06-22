using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "article_embeddings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ArticleId = table.Column<string>(type: "TEXT", nullable: false),
                    Embedding = table.Column<byte[]>(type: "BLOB", nullable: false),
                    EmbeddingNorm = table.Column<double>(type: "REAL", nullable: false),
                    ModelName = table.Column<string>(type: "TEXT", nullable: false),
                    TextHash = table.Column<string>(type: "TEXT", nullable: false),
                    Dimensions = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_embeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_article_embeddings_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_article_embeddings_ArticleId",
                table: "article_embeddings",
                column: "ArticleId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "article_embeddings");
        }
    }
}
