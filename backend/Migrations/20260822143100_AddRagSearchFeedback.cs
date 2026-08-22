using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260822143100_AddRagSearchFeedback")]
public partial class AddRagSearchFeedback : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "RagAnswerHash", table: "search_queries", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RagFeedback", table: "search_queries", type: "character varying(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "RagFeedbackAt", table: "search_queries", type: "timestamp without time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "RagFeedbackReason", table: "search_queries", type: "character varying(40)", maxLength: 40, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RagGroundingStatus", table: "search_queries", type: "character varying(40)", maxLength: 40, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RagIndexProfile", table: "search_queries", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RagPromptVersion", table: "search_queries", type: "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RagTraceId", table: "search_queries", type: "character varying(64)", maxLength: 64, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "RagAnswerHash", table: "search_queries");
        migrationBuilder.DropColumn(name: "RagFeedback", table: "search_queries");
        migrationBuilder.DropColumn(name: "RagFeedbackAt", table: "search_queries");
        migrationBuilder.DropColumn(name: "RagFeedbackReason", table: "search_queries");
        migrationBuilder.DropColumn(name: "RagGroundingStatus", table: "search_queries");
        migrationBuilder.DropColumn(name: "RagIndexProfile", table: "search_queries");
        migrationBuilder.DropColumn(name: "RagPromptVersion", table: "search_queries");
        migrationBuilder.DropColumn(name: "RagTraceId", table: "search_queries");
    }
}
