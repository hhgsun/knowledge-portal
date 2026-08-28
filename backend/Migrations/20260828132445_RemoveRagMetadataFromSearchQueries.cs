using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRagMetadataFromSearchQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rag_answer_hash",
                table: "search_queries");

            migrationBuilder.DropColumn(
                name: "rag_feedback",
                table: "search_queries");

            migrationBuilder.DropColumn(
                name: "rag_feedback_at",
                table: "search_queries");

            migrationBuilder.DropColumn(
                name: "rag_feedback_reason",
                table: "search_queries");

            migrationBuilder.DropColumn(
                name: "rag_grounding_status",
                table: "search_queries");

            migrationBuilder.DropColumn(
                name: "rag_index_profile",
                table: "search_queries");

            migrationBuilder.DropColumn(
                name: "rag_prompt_version",
                table: "search_queries");

            migrationBuilder.DropColumn(
                name: "rag_reranker",
                table: "search_queries");

            migrationBuilder.DropColumn(
                name: "rag_retrieval_version",
                table: "search_queries");

            migrationBuilder.DropColumn(
                name: "rag_trace_id",
                table: "search_queries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "rag_answer_hash",
                table: "search_queries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_feedback",
                table: "search_queries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "rag_feedback_at",
                table: "search_queries",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_feedback_reason",
                table: "search_queries",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_grounding_status",
                table: "search_queries",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_index_profile",
                table: "search_queries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_prompt_version",
                table: "search_queries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_reranker",
                table: "search_queries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_retrieval_version",
                table: "search_queries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rag_trace_id",
                table: "search_queries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }
    }
}
