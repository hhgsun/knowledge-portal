using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeparateSearchAndAssistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "clicked_article_id",
                table: "assistant_interactions",
                type: "character varying(21)",
                maxLength: 21,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_answer_hash",
                table: "assistant_interactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_grounding_status",
                table: "assistant_interactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_index_profile",
                table: "assistant_interactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_prompt_version",
                table: "assistant_interactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_reranker",
                table: "assistant_interactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_retrieval_version",
                table: "assistant_interactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_trace_id",
                table: "assistant_interactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "clicked_article_id",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "rag_answer_hash",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "rag_grounding_status",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "rag_index_profile",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "rag_prompt_version",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "rag_reranker",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "rag_retrieval_version",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "rag_trace_id",
                table: "assistant_interactions");
        }
    }
}
