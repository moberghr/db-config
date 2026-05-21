-- DbConfig initial schema for PostgreSQL.
-- Idempotent: every statement uses IF NOT EXISTS so the script can be re-applied safely.
-- {schema} is substituted at apply time by PostgreSqlDbConfigMigrator.
-- Identifier casing must match EF Core defaults + snake_case convention
-- (class names: ConfigEntry -> config_entries; AuditEntry -> audit_entries; etc).

-- ---------- Schema ----------
CREATE SCHEMA IF NOT EXISTS "{schema}";

-- ---------- config_entries ----------
CREATE TABLE IF NOT EXISTS "{schema}"."config_entries"
(
    "id"            uuid                      NOT NULL,
    "scope"         varchar(128) COLLATE "C"  NOT NULL,
    "environment"   varchar(64)  COLLATE "C"  NOT NULL,
    "tenant_id"     varchar(128) COLLATE "C"  NOT NULL,
    "key"           varchar(512) COLLATE "C"  NOT NULL,
    "value"         text                      NULL,
    "is_secret"     boolean                   NOT NULL DEFAULT false,
    "modified_utc"  timestamp with time zone  NOT NULL,
    "modified_by"   varchar(256)              NULL,
    CONSTRAINT "pk_config_entries" PRIMARY KEY ("id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "ix_config_entries_scope_environment_tenant_id_key"
    ON "{schema}"."config_entries" ("scope", "environment", "tenant_id", "key");

CREATE INDEX IF NOT EXISTS "ix_config_entries_scope_environment_tenant_id_modified_utc"
    ON "{schema}"."config_entries" ("scope", "environment", "tenant_id", "modified_utc" DESC);

-- ---------- audit_entries ----------
CREATE TABLE IF NOT EXISTS "{schema}"."audit_entries"
(
    "id"            uuid                      NOT NULL,
    "scope"         varchar(128) COLLATE "C"  NOT NULL,
    "environment"   varchar(64)  COLLATE "C"  NOT NULL,
    "tenant_id"     varchar(128) COLLATE "C"  NOT NULL,
    "key"           varchar(512) COLLATE "C"  NOT NULL,
    "old_value"     text                      NULL,
    "new_value"     text                      NULL,
    "is_secret"     boolean                   NOT NULL,
    "action"        varchar(16)               NOT NULL,
    "modified_utc"  timestamp with time zone  NOT NULL,
    "modified_by"   varchar(256)              NULL,
    CONSTRAINT "pk_audit_entries" PRIMARY KEY ("id")
);

CREATE INDEX IF NOT EXISTS "ix_audit_entries_scope_environment_tenant_id_key_modified_utc"
    ON "{schema}"."audit_entries" ("scope", "environment", "tenant_id", "key", "modified_utc" DESC);
