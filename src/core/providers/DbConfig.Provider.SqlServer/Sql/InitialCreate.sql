-- DbConfig initial schema for SQL Server.
-- Idempotent: every statement is guarded so the script can be re-applied safely.
-- {schema} is substituted at apply time by SqlServerDbConfigMigrator.
-- Identifier casing must match EF Core defaults (class names: ConfigEntries, AuditEntries).

-- ---------- Schema ----------
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{schema}')
    EXEC(N'CREATE SCHEMA [{schema}]');
GO

-- ---------- ConfigEntries ----------
IF NOT EXISTS (
    SELECT 1 FROM sys.tables t
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = N'{schema}' AND t.name = N'ConfigEntries')
BEGIN
    CREATE TABLE [{schema}].[ConfigEntries]
    (
        [Id]           uniqueidentifier              NOT NULL CONSTRAINT [PK_ConfigEntries] PRIMARY KEY,
        [Scope]        nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Environment]  nvarchar(64)  COLLATE Latin1_General_100_BIN2 NOT NULL,
        [TenantId]     nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Key]          nvarchar(512) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Value]        nvarchar(max)                 NULL,
        [IsSecret]     bit                           NOT NULL CONSTRAINT [DF_ConfigEntries_IsSecret] DEFAULT 0,
        [ModifiedUtc]  datetimeoffset                NOT NULL,
        [ModifiedBy]   nvarchar(256)                 NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_ConfigEntries_Scope_Environment_TenantId_Key'
      AND object_id = OBJECT_ID(N'[{schema}].[ConfigEntries]'))
BEGIN
    CREATE UNIQUE INDEX [IX_ConfigEntries_Scope_Environment_TenantId_Key]
        ON [{schema}].[ConfigEntries] ([Scope], [Environment], [TenantId], [Key]);
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_ConfigEntries_Scope_Environment_TenantId_ModifiedUtc'
      AND object_id = OBJECT_ID(N'[{schema}].[ConfigEntries]'))
BEGIN
    CREATE INDEX [IX_ConfigEntries_Scope_Environment_TenantId_ModifiedUtc]
        ON [{schema}].[ConfigEntries] ([Scope], [Environment], [TenantId], [ModifiedUtc] DESC);
END;
GO

-- ---------- AuditEntries ----------
IF NOT EXISTS (
    SELECT 1 FROM sys.tables t
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = N'{schema}' AND t.name = N'AuditEntries')
BEGIN
    CREATE TABLE [{schema}].[AuditEntries]
    (
        [Id]           uniqueidentifier              NOT NULL CONSTRAINT [PK_AuditEntries] PRIMARY KEY,
        [Scope]        nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Environment]  nvarchar(64)  COLLATE Latin1_General_100_BIN2 NOT NULL,
        [TenantId]     nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Key]          nvarchar(512) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [OldValue]     nvarchar(max)                 NULL,
        [NewValue]     nvarchar(max)                 NULL,
        [IsSecret]     bit                           NOT NULL,
        [Action]       nvarchar(16)                  NOT NULL,
        [ModifiedUtc]  datetimeoffset                NOT NULL,
        [ModifiedBy]   nvarchar(256)                 NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_AuditEntries_Scope_Environment_TenantId_Key_ModifiedUtc'
      AND object_id = OBJECT_ID(N'[{schema}].[AuditEntries]'))
BEGIN
    CREATE INDEX [IX_AuditEntries_Scope_Environment_TenantId_Key_ModifiedUtc]
        ON [{schema}].[AuditEntries] ([Scope], [Environment], [TenantId], [Key], [ModifiedUtc] DESC);
END;
GO
