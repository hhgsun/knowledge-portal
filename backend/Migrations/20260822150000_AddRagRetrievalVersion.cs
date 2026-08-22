using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260822150000_AddRagRetrievalVersion")]
public partial class AddRagRetrievalVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "RagReranker", table: "search_queries",
            type: "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RagRetrievalVersion", table: "search_queries",
            type: "character varying(100)", maxLength: 100, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "RagReranker", table: "search_queries");
        migrationBuilder.DropColumn(name: "RagRetrievalVersion", table: "search_queries");
    }
}
