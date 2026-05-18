using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbConfig.Provider.PostgreSql.Migrations;

/// <summary>
/// Applies case-sensitive "C" collation to AppName, Environment, and Key columns on
/// both DbConfig_Entries and DbConfig_AuditEntries.
///
/// PostgreSQL requires indexes that include the affected columns to be dropped before
/// the ALTER COLUMN and re-created afterwards (text collation change is not in-place).
/// </summary>
public partial class CaseSensitiveScopeColumns : Migration
{
    private const string BinaryCollation = "C";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_DbConfig_Entries_AppName_Environment_ModifiedUtc",
            table: "DbConfig_Entries");

        migrationBuilder.DropIndex(
            name: "UX_DbConfig_Entries_AppName_Environment_Key",
            table: "DbConfig_Entries");

        migrationBuilder.AlterColumn<string>(
            name: "AppName",
            table: "DbConfig_Entries",
            type: "text",
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<string>(
            name: "Environment",
            table: "DbConfig_Entries",
            type: "text",
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<string>(
            name: "Key",
            table: "DbConfig_Entries",
            type: "text",
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_Entries_AppName_Environment_ModifiedUtc",
            table: "DbConfig_Entries",
            columns: ["AppName", "Environment", "ModifiedUtc"],
            descending: [false, false, true]);

        migrationBuilder.CreateIndex(
            name: "UX_DbConfig_Entries_AppName_Environment_Key",
            table: "DbConfig_Entries",
            columns: ["AppName", "Environment", "Key"],
            unique: true);

        migrationBuilder.DropIndex(
            name: "IX_DbConfig_AuditEntries_AppName_Environment_Key_ModifiedUtc",
            table: "DbConfig_AuditEntries");

        migrationBuilder.AlterColumn<string>(
            name: "AppName",
            table: "DbConfig_AuditEntries",
            type: "text",
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<string>(
            name: "Environment",
            table: "DbConfig_AuditEntries",
            type: "text",
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<string>(
            name: "Key",
            table: "DbConfig_AuditEntries",
            type: "text",
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_AuditEntries_AppName_Environment_Key_ModifiedUtc",
            table: "DbConfig_AuditEntries",
            columns: ["AppName", "Environment", "Key", "ModifiedUtc"],
            descending: [false, false, false, true]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_DbConfig_Entries_AppName_Environment_ModifiedUtc",
            table: "DbConfig_Entries");

        migrationBuilder.DropIndex(
            name: "UX_DbConfig_Entries_AppName_Environment_Key",
            table: "DbConfig_Entries");

        migrationBuilder.AlterColumn<string>(
            name: "AppName",
            table: "DbConfig_Entries",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldCollation: BinaryCollation);

        migrationBuilder.AlterColumn<string>(
            name: "Environment",
            table: "DbConfig_Entries",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldCollation: BinaryCollation);

        migrationBuilder.AlterColumn<string>(
            name: "Key",
            table: "DbConfig_Entries",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldCollation: BinaryCollation);

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_Entries_AppName_Environment_ModifiedUtc",
            table: "DbConfig_Entries",
            columns: ["AppName", "Environment", "ModifiedUtc"],
            descending: [false, false, true]);

        migrationBuilder.CreateIndex(
            name: "UX_DbConfig_Entries_AppName_Environment_Key",
            table: "DbConfig_Entries",
            columns: ["AppName", "Environment", "Key"],
            unique: true);

        migrationBuilder.DropIndex(
            name: "IX_DbConfig_AuditEntries_AppName_Environment_Key_ModifiedUtc",
            table: "DbConfig_AuditEntries");

        migrationBuilder.AlterColumn<string>(
            name: "AppName",
            table: "DbConfig_AuditEntries",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldCollation: BinaryCollation);

        migrationBuilder.AlterColumn<string>(
            name: "Environment",
            table: "DbConfig_AuditEntries",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldCollation: BinaryCollation);

        migrationBuilder.AlterColumn<string>(
            name: "Key",
            table: "DbConfig_AuditEntries",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldCollation: BinaryCollation);

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_AuditEntries_AppName_Environment_Key_ModifiedUtc",
            table: "DbConfig_AuditEntries",
            columns: ["AppName", "Environment", "Key", "ModifiedUtc"],
            descending: [false, false, false, true]);
    }
}
