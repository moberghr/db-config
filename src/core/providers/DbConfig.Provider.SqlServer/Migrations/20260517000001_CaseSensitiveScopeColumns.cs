using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbConfig.Provider.SqlServer.Migrations;

/// <summary>
/// Applies case-sensitive binary collation (Latin1_General_100_BIN2) to AppName,
/// Environment, and Key columns on both DbConfig_Entries and DbConfig_AuditEntries.
///
/// SQL Server requires indexes that reference the altered columns to be dropped before
/// the ALTER COLUMN and re-created afterwards.
/// </summary>
public partial class CaseSensitiveScopeColumns : Migration
{
    private const string BinaryCollation = "Latin1_General_100_BIN2";
    private const string DefaultCollation = "Latin1_General_CI_AS";

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
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128);

        migrationBuilder.AlterColumn<string>(
            name: "Environment",
            table: "DbConfig_Entries",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(64)",
            oldMaxLength: 64);

        migrationBuilder.AlterColumn<string>(
            name: "Key",
            table: "DbConfig_Entries",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(512)",
            oldMaxLength: 512);

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
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128);

        migrationBuilder.AlterColumn<string>(
            name: "Environment",
            table: "DbConfig_AuditEntries",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(64)",
            oldMaxLength: 64);

        migrationBuilder.AlterColumn<string>(
            name: "Key",
            table: "DbConfig_AuditEntries",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: false,
            collation: BinaryCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(512)",
            oldMaxLength: 512);

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
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            collation: DefaultCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128,
            oldCollation: BinaryCollation);

        migrationBuilder.AlterColumn<string>(
            name: "Environment",
            table: "DbConfig_Entries",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            collation: DefaultCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(64)",
            oldMaxLength: 64,
            oldCollation: BinaryCollation);

        migrationBuilder.AlterColumn<string>(
            name: "Key",
            table: "DbConfig_Entries",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: false,
            collation: DefaultCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(512)",
            oldMaxLength: 512,
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
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            collation: DefaultCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128,
            oldCollation: BinaryCollation);

        migrationBuilder.AlterColumn<string>(
            name: "Environment",
            table: "DbConfig_AuditEntries",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            collation: DefaultCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(64)",
            oldMaxLength: 64,
            oldCollation: BinaryCollation);

        migrationBuilder.AlterColumn<string>(
            name: "Key",
            table: "DbConfig_AuditEntries",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: false,
            collation: DefaultCollation,
            oldClrType: typeof(string),
            oldType: "nvarchar(512)",
            oldMaxLength: 512,
            oldCollation: BinaryCollation);

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_AuditEntries_AppName_Environment_Key_ModifiedUtc",
            table: "DbConfig_AuditEntries",
            columns: ["AppName", "Environment", "Key", "ModifiedUtc"],
            descending: [false, false, false, true]);
    }
}
