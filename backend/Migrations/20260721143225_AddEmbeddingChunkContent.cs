using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingChunkContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Content",
                table: "article_embeddings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Content",
                table: "article_embeddings");
        }
    }
}
