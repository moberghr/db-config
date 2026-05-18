using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbConfig.Provider.SqlServer.Migrations;

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
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AppName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Environment = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Key = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                IsSecret = table.Column<bool>(type: "bit", nullable: false),
                Action = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                ModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
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
