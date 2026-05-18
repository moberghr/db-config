using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbConfig.Provider.PostgreSql.Migrations;

/// <inheritdoc />
public partial class AddAuditEntries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DbConfig_AuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AppName = table.Column<string>(type: "text", nullable: false),
                Environment = table.Column<string>(type: "text", nullable: false),
                Key = table.Column<string>(type: "text", nullable: false),
                OldValue = table.Column<string>(type: "text", nullable: true),
                NewValue = table.Column<string>(type: "text", nullable: true),
                IsSecret = table.Column<bool>(type: "boolean", nullable: false),
                Action = table.Column<string>(type: "text", nullable: false),
                ModifiedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ModifiedBy = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_DbConfig_AuditEntries", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_AuditEntries_AppName_Environment_Key_ModifiedUtc",
            table: "DbConfig_AuditEntries",
            columns: ["AppName", "Environment", "Key", "ModifiedUtc"],
            descending: [false, false, false, true]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DbConfig_AuditEntries");
    }
}
