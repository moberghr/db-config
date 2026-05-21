using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbConfig.Provider.SqlServer.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    private const string ScopeCollation = "Latin1_General_100_BIN2";

    private readonly string _schema;

    /// <summary>
    /// Initializes a new instance of the <see cref="InitialCreate"/> class with the configured
    /// schema. Invoked by <c>DbConfigMigrationsAssembly</c> with the value of
    /// <c>DbConfigOptionsExtension.Schema</c>. Passing <see langword="null"/> uses the database
    /// default schema (<c>dbo</c>).
    /// </summary>
    public InitialCreate(string schema)
    {
        _schema = schema;
    }

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (_schema is not null)
        {
            migrationBuilder.EnsureSchema(name: _schema);
        }

        migrationBuilder.CreateTable(
            name: "DbConfig_Entries",
            schema: _schema,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Scope = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: ScopeCollation),
                Environment = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: ScopeCollation),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: ScopeCollation),
                Key = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: ScopeCollation),
                Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                IsSecret = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                ModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_DbConfig_Entries", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_Entries_Scope_Environment_TenantId_ModifiedUtc",
            schema: _schema,
            table: "DbConfig_Entries",
            columns: ["Scope", "Environment", "TenantId", "ModifiedUtc"]);

        migrationBuilder.CreateIndex(
            name: "UX_DbConfig_Entries_Scope_Environment_TenantId_Key",
            schema: _schema,
            table: "DbConfig_Entries",
            columns: ["Scope", "Environment", "TenantId", "Key"],
            unique: true);

        migrationBuilder.CreateTable(
            name: "DbConfig_AuditEntries",
            schema: _schema,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Scope = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: ScopeCollation),
                Environment = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: ScopeCollation),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: ScopeCollation),
                Key = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: ScopeCollation),
                OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                IsSecret = table.Column<bool>(type: "bit", nullable: false),
                Action = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                ModifiedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_DbConfig_AuditEntries", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_DbConfig_Audit_Scope_Environment_TenantId_Key_ModifiedUtc",
            schema: _schema,
            table: "DbConfig_AuditEntries",
            columns: ["Scope", "Environment", "TenantId", "Key", "ModifiedUtc"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DbConfig_AuditEntries", schema: _schema);
        migrationBuilder.DropTable(name: "DbConfig_Entries", schema: _schema);
    }
}
