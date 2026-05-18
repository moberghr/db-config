using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbConfig.Provider.SqlServer.Migrations;

/// <summary>
/// Adds <c>TenantId nvarchar(128) NOT NULL DEFAULT ''</c> to both <c>DbConfig_Entries</c>
/// and <c>DbConfig_AuditEntries</c>, and updates the unique constraint and watermark /
/// history indexes to include the new column.
///
/// SQL Server requires index drops before the column can be used in new indexes. The column
/// is added with the binary collation (Latin1_General_100_BIN2) to match the other scope columns.
/// </summary>
public partial class AddTenantId : Migration
{
    private const string BinaryCollation = "Latin1_General_100_BIN2";

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
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: string.Empty);

        migrationBuilder.AlterColumn<string>(
            name: "TenantId",
            table: "DbConfig_Entries",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128);

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
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: string.Empty);

        migrationBuilder.AlterColumn<string>(
            name: "TenantId",
            table: "DbConfig_AuditEntries",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128);

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
