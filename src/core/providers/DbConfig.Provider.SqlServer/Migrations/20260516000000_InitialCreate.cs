using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbConfig.Provider.SqlServer.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DbConfig_Entries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AppName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Environment = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Key = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                IsSecret = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                ModifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_DbConfig_Entries", x => x.Id));

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
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DbConfig_Entries");
    }
}
