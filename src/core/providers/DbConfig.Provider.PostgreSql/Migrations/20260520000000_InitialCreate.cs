using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbConfig.Provider.PostgreSql.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    private const string BinaryCollation = "C";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DbConfig_Entries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Scope = table.Column<string>(type: "text", maxLength: 128, nullable: false, collation: BinaryCollation),
                Environment = table.Column<string>(type: "text", maxLength: 64, nullable: false, collation: BinaryCollation),
                TenantId = table.Column<string>(type: "text", maxLength: 128, nullable: false, collation: BinaryCollation),
                Key = table.Column<string>(type: "text", maxLength: 512, nullable: false, collation: BinaryCollation),
                Value = table.Column<string>(type: "text", nullable: true),
                IsSecret = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                ModifiedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ModifiedBy = table.Column<string>(type: "text", maxLength: 256, nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_DbConfig_Entries", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_Entries_Scope_Environment_TenantId_ModifiedUtc",
            table: "DbConfig_Entries",
            columns: ["Scope", "Environment", "TenantId", "ModifiedUtc"]);

        migrationBuilder.CreateIndex(
            name: "UX_DbConfig_Entries_Scope_Environment_TenantId_Key",
            table: "DbConfig_Entries",
            columns: ["Scope", "Environment", "TenantId", "Key"],
            unique: true);

        migrationBuilder.CreateTable(
            name: "DbConfig_AuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Scope = table.Column<string>(type: "text", maxLength: 128, nullable: false, collation: BinaryCollation),
                Environment = table.Column<string>(type: "text", maxLength: 64, nullable: false, collation: BinaryCollation),
                TenantId = table.Column<string>(type: "text", maxLength: 128, nullable: false, collation: BinaryCollation),
                Key = table.Column<string>(type: "text", maxLength: 512, nullable: false, collation: BinaryCollation),
                OldValue = table.Column<string>(type: "text", nullable: true),
                NewValue = table.Column<string>(type: "text", nullable: true),
                IsSecret = table.Column<bool>(type: "boolean", nullable: false),
                Action = table.Column<string>(type: "text", maxLength: 16, nullable: false),
                ModifiedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ModifiedBy = table.Column<string>(type: "text", maxLength: 256, nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_DbConfig_AuditEntries", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_Audit_Scope_Environment_TenantId_Key_ModifiedUtc",
            table: "DbConfig_AuditEntries",
            columns: ["Scope", "Environment", "TenantId", "Key", "ModifiedUtc"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DbConfig_AuditEntries");
        migrationBuilder.DropTable(name: "DbConfig_Entries");
    }
}
