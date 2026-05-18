using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbConfig.Provider.PostgreSql.Migrations;

/// <summary>
/// Adds <c>TenantId text COLLATE "C" NOT NULL DEFAULT ''</c> to both <c>DbConfig_Entries</c>
/// and <c>DbConfig_AuditEntries</c>, and updates the unique constraint and watermark /
/// history indexes to include the new column.
///
/// PostgreSQL requires existing indexes to be dropped and recreated when the index columns
/// change. The new column is added directly with the "C" collation to match the other scope columns.
/// </summary>
public partial class AddTenantId : Migration
{
    private const string BinaryCollation = "C";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_DbConfig_Entries_AppName_Environment_Key",
            table: "DbConfig_Entries");

        migrationBuilder.DropIndex(
            name: "IX_DbConfig_Entries_AppName_Environment_ModifiedUtc",
            table: "DbConfig_Entries");

        migrationBuilder.AddColumn<string>(
            name: "TenantId",
            table: "DbConfig_Entries",
            type: "text",
            nullable: false,
            defaultValue: string.Empty,
            collation: BinaryCollation);

        migrationBuilder.CreateIndex(
            name: "UX_DbConfig_Entries_AppName_Environment_TenantId_Key",
            table: "DbConfig_Entries",
            columns: ["AppName", "Environment", "TenantId", "Key"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_Entries_AppName_Environment_TenantId_ModifiedUtc",
            table: "DbConfig_Entries",
            columns: ["AppName", "Environment", "TenantId", "ModifiedUtc"],
            descending: [false, false, false, true]);

        migrationBuilder.DropIndex(
            name: "IX_DbConfig_AuditEntries_AppName_Environment_Key_ModifiedUtc",
            table: "DbConfig_AuditEntries");

        migrationBuilder.AddColumn<string>(
            name: "TenantId",
            table: "DbConfig_AuditEntries",
            type: "text",
            nullable: false,
            defaultValue: string.Empty,
            collation: BinaryCollation);

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_Audit_AppName_Environment_TenantId_Key_ModifiedUtc",
            table: "DbConfig_AuditEntries",
            columns: ["AppName", "Environment", "TenantId", "Key", "ModifiedUtc"],
            descending: [false, false, false, false, true]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_DbConfig_Audit_AppName_Environment_TenantId_Key_ModifiedUtc",
            table: "DbConfig_AuditEntries");

        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "DbConfig_AuditEntries");

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_AuditEntries_AppName_Environment_Key_ModifiedUtc",
            table: "DbConfig_AuditEntries",
            columns: ["AppName", "Environment", "Key", "ModifiedUtc"],
            descending: [false, false, false, true]);

        migrationBuilder.DropIndex(
            name: "UX_DbConfig_Entries_AppName_Environment_TenantId_Key",
            table: "DbConfig_Entries");

        migrationBuilder.DropIndex(
            name: "IX_DbConfig_Entries_AppName_Environment_TenantId_ModifiedUtc",
            table: "DbConfig_Entries");

        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "DbConfig_Entries");

        migrationBuilder.CreateIndex(
            name: "UX_DbConfig_Entries_AppName_Environment_Key",
            table: "DbConfig_Entries",
            columns: ["AppName", "Environment", "Key"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_Entries_AppName_Environment_ModifiedUtc",
            table: "DbConfig_Entries",
            columns: ["AppName", "Environment", "ModifiedUtc"],
            descending: [false, false, true]);
    }
}
