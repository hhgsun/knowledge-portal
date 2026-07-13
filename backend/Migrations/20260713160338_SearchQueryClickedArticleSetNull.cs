using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class SearchQueryClickedArticleSetNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_search_queries_articles_ClickedArticleId",
                table: "search_queries");

            migrationBuilder.AddForeignKey(
                name: "FK_search_queries_articles_ClickedArticleId",
                table: "search_queries",
                column: "ClickedArticleId",
                principalTable: "articles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_search_queries_articles_ClickedArticleId",
                table: "search_queries");

            migrationBuilder.AddForeignKey(
                name: "FK_search_queries_articles_ClickedArticleId",
                table: "search_queries",
                column: "ClickedArticleId",
                principalTable: "articles",
                principalColumn: "Id");
        }
    }
}
