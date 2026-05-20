/**
 * Demo data for db-config UI demo mode.
 *
 * All timestamps are deterministic: computed once at module load from `NOW`
 * so every browser session produces the same relative times.
 */

import type { ConfigEntry, ConfigAuditEntry, ConfigAuditAction } from '@/api/entries'

// Captured once per module load — makes timestamps deterministic within a session.
const NOW = Date.now()

/** Returns an ISO 8601 string for `days` days before NOW. */
function daysAgo(days: number): string {
  return new Date(NOW - days * 86_400_000).toISOString()
}

export const DEMO_TENANTS = ['Acme', 'Globex'] as const

// ============================================================
// Stable IDs for Playwright screenshot tests (B36)
// ============================================================

export const DEMO_IDS = {
  stripeApiKey: 'PaymentService:Stripe:ApiKey',
  stripeWebhookSecret: 'PaymentService:Stripe:WebhookSecret',
  connectionString: 'PaymentService:ConnectionStrings:Default',
  featuresNewCheckout: 'PaymentService:Features:NewCheckout',
  timeoutsHttpSeconds: 'PaymentService:Timeouts:HttpSeconds',
  emailNotificationsReplyTo: 'PaymentService:EmailNotifications:ReplyTo',
  loggingLogLevel: 'Shared:Logging:LogLevel',
  metricsEndpointUrl: 'Shared:MetricsEndpoint:Url',
  rateLimitsRequestsPerSecond: 'Shared:RateLimits:RequestsPerSecond',
  emailNotificationsSmtpHost: 'Shared:EmailNotifications:SmtpHost',
  emailNotificationsSmtpPassword: 'Shared:EmailNotifications:SmtpPassword',
  emailNotificationsFromAddress: 'Shared:EmailNotifications:FromAddress',
  cacheDefaultTtlSeconds: 'PlatformDefaults:Cache:DefaultTtlSeconds',
  cacheMaxItems: 'PlatformDefaults:Cache:MaxItems',
  loggingMinLevel: 'PlatformDefaults:Logging:MinLevel',
  diagnosticsEndpointUrl: 'PlatformDefaults:DiagnosticsEndpoint:Url',
  complianceDataRetentionDays: 'PlatformDefaults:Compliance:DataRetentionDays',
  // Multi-level keys added for tree view (B48)
  stripePaymentApiKey: 'PaymentService:Stripe:Payment:ApiKey',
  stripePaymentWebhookSecret: 'PaymentService:Stripe:Payment:WebhookSecret',
  stripeAuthOauthClientId: 'PaymentService:Stripe:Auth:OauthClientId',
  stripeAuthOauthClientSecret: 'PaymentService:Stripe:Auth:OauthClientSecret',
  twilioSmsAccountSid: 'PaymentService:Twilio:Sms:AccountSid',
  twilioSmsAuthToken: 'PaymentService:Twilio:Sms:AuthToken',
  twilioVoiceWebhookUrl: 'PaymentService:Twilio:Voice:WebhookUrl',
} as const

// ============================================================
// ConfigEntry snapshot
// ============================================================

const ENV = 'Production'

/** All demo entries across three scopes plus tenant-specific overrides. */
export const DEMO_ENTRIES: ConfigEntry[] = [
  // --- PaymentService (own scope, global tenant) ---
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: '',
    key: 'ConnectionStrings:Default',
    value: 'Server=db.payments.example.com;Port=5432;Database=payments;User Id=app;Password=s3cr3t!',
    isSecret: true,
    modifiedUtc: daysAgo(2),
    modifiedBy: 'automation-bot@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: '',
    key: 'Stripe:ApiKey',
    value: 'sk_test_DEMO_REPLACE_ME_NOT_REAL',
    isSecret: true,
    modifiedUtc: daysAgo(2),
    modifiedBy: 'automation-bot@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: '',
    key: 'Stripe:WebhookSecret',
    value: 'whsec_DEMO_REPLACE_ME_NOT_REAL',
    isSecret: true,
    modifiedUtc: daysAgo(7),
    modifiedBy: 'payments-lead@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: '',
    key: 'Features:NewCheckout',
    value: 'true',
    isSecret: false,
    modifiedUtc: daysAgo(5),
    modifiedBy: 'payments-lead@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: '',
    key: 'Timeouts:HttpSeconds',
    value: '30',
    isSecret: false,
    modifiedUtc: daysAgo(14),
    modifiedBy: 'platform-admin@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: '',
    key: 'EmailNotifications:ReplyTo',
    value: 'noreply@example.com',
    isSecret: false,
    modifiedUtc: daysAgo(14),
    modifiedBy: 'platform-admin@example.com',
  },
  // Multi-level Stripe entries (3 levels deep) — for tree view demo (B48)
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: '',
    key: 'Stripe:Payment:ApiKey',
    value: 'sk_test_payment_DEMO_NOT_REAL',
    isSecret: true,
    modifiedUtc: daysAgo(3),
    modifiedBy: 'payments-lead@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: '',
    key: 'Stripe:Payment:WebhookSecret',
    value: 'whsec_payment_DEMO_NOT_REAL',
    isSecret: true,
    modifiedUtc: daysAgo(7),
    modifiedBy: 'payments-lead@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: '',
    key: 'Stripe:Auth:OauthClientId',
    value: 'ca_stripe_oauth_client_abc123',
    isSecret: false,
    modifiedUtc: daysAgo(10),
    modifiedBy: 'payments-lead@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: '',
    key: 'Stripe:Auth:OauthClientSecret',
    value: 'cs_stripe_oauth_secret_xyz789',
    isSecret: true,
    modifiedUtc: daysAgo(10),
    modifiedBy: 'payments-lead@example.com',
  },
  // Multi-level Twilio entries (3 levels deep) — for tree view demo (B48)
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: '',
    key: 'Twilio:Sms:AccountSid',
    value: 'ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx',
    isSecret: false,
    modifiedUtc: daysAgo(5),
    modifiedBy: 'automation-bot@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: '',
    key: 'Twilio:Sms:AuthToken',
    value: 'twilio_auth_token_secret_value',
    isSecret: true,
    modifiedUtc: daysAgo(5),
    modifiedBy: 'automation-bot@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: '',
    key: 'Twilio:Voice:WebhookUrl',
    value: 'https://voice.example.com/webhook/twilio',
    isSecret: false,
    modifiedUtc: daysAgo(12),
    modifiedBy: 'payments-lead@example.com',
  },
  // --- Shared (cross-team scope, global tenant) ---
  {
    scope: 'Shared',
    environment: ENV,
    tenantId: '',
    key: 'Logging:LogLevel',
    value: 'Information',
    isSecret: false,
    modifiedUtc: daysAgo(20),
    modifiedBy: 'platform-admin@example.com',
  },
  {
    scope: 'Shared',
    environment: ENV,
    tenantId: '',
    key: 'MetricsEndpoint:Url',
    value: 'https://otel.example.com:4317',
    isSecret: false,
    modifiedUtc: daysAgo(18),
    modifiedBy: 'platform-admin@example.com',
  },
  {
    scope: 'Shared',
    environment: ENV,
    tenantId: '',
    key: 'RateLimits:RequestsPerSecond',
    value: '100',
    isSecret: false,
    modifiedUtc: daysAgo(10),
    modifiedBy: 'dev-self@example.com',
  },
  {
    scope: 'Shared',
    environment: ENV,
    tenantId: '',
    key: 'EmailNotifications:SmtpHost',
    value: 'smtp.example.com',
    isSecret: false,
    modifiedUtc: daysAgo(22),
    modifiedBy: 'platform-admin@example.com',
  },
  {
    scope: 'Shared',
    environment: ENV,
    tenantId: '',
    key: 'EmailNotifications:SmtpPassword',
    value: 'Smtp@Passw0rd!2024',
    isSecret: true,
    modifiedUtc: daysAgo(3),
    modifiedBy: 'automation-bot@example.com',
  },
  {
    scope: 'Shared',
    environment: ENV,
    tenantId: '',
    key: 'EmailNotifications:FromAddress',
    value: 'noreply@example.com',
    isSecret: false,
    modifiedUtc: daysAgo(22),
    modifiedBy: 'platform-admin@example.com',
  },
  // --- PlatformDefaults (org-wide baseline, global tenant) ---
  {
    scope: 'PlatformDefaults',
    environment: ENV,
    tenantId: '',
    key: 'Cache:DefaultTtlSeconds',
    value: '300',
    isSecret: false,
    modifiedUtc: daysAgo(28),
    modifiedBy: 'platform-admin@example.com',
  },
  {
    scope: 'PlatformDefaults',
    environment: ENV,
    tenantId: '',
    key: 'Cache:MaxItems',
    value: '10000',
    isSecret: false,
    modifiedUtc: daysAgo(28),
    modifiedBy: 'platform-admin@example.com',
  },
  {
    scope: 'PlatformDefaults',
    environment: ENV,
    tenantId: '',
    key: 'Logging:MinLevel',
    value: 'Warning',
    isSecret: false,
    modifiedUtc: daysAgo(25),
    modifiedBy: 'platform-admin@example.com',
  },
  {
    scope: 'PlatformDefaults',
    environment: ENV,
    tenantId: '',
    key: 'DiagnosticsEndpoint:Url',
    value: 'https://diag.example.com/health',
    isSecret: false,
    modifiedUtc: daysAgo(25),
    modifiedBy: 'platform-admin@example.com',
  },
  {
    scope: 'PlatformDefaults',
    environment: ENV,
    tenantId: '',
    key: 'Compliance:DataRetentionDays',
    value: '90',
    isSecret: false,
    modifiedUtc: daysAgo(30),
    modifiedBy: 'platform-admin@example.com',
  },
  // --- Acme tenant overrides on PaymentService ---
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: 'Acme',
    key: 'Stripe:ApiKey',
    value: 'sk_test_acme_DEMO_NOT_REAL',
    isSecret: true,
    modifiedUtc: daysAgo(4),
    modifiedBy: 'acme-admin@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: 'Acme',
    key: 'Stripe:WebhookSecret',
    value: 'whsec_acme_DEMO_NOT_REAL',
    isSecret: true,
    modifiedUtc: daysAgo(4),
    modifiedBy: 'acme-admin@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: 'Acme',
    key: 'Features:NewCheckout',
    value: 'false',
    isSecret: false,
    modifiedUtc: daysAgo(6),
    modifiedBy: 'acme-admin@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: 'Acme',
    key: 'Timeouts:HttpSeconds',
    value: '60',
    isSecret: false,
    modifiedUtc: daysAgo(8),
    modifiedBy: 'acme-admin@example.com',
  },
  // --- Globex tenant overrides on PaymentService ---
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: 'Globex',
    key: 'Stripe:ApiKey',
    value: 'sk_test_globex_DEMO_NOT_REAL',
    isSecret: true,
    modifiedUtc: daysAgo(3),
    modifiedBy: 'globex-admin@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: 'Globex',
    key: 'Stripe:WebhookSecret',
    value: 'whsec_globex_DEMO_NOT_REAL',
    isSecret: true,
    modifiedUtc: daysAgo(3),
    modifiedBy: 'globex-admin@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: 'Globex',
    key: 'EmailNotifications:ReplyTo',
    value: 'noreply@globex.example.com',
    isSecret: false,
    modifiedUtc: daysAgo(9),
    modifiedBy: 'globex-admin@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: 'Globex',
    key: 'Features:NewCheckout',
    value: 'true',
    isSecret: false,
    modifiedUtc: daysAgo(1),
    modifiedBy: 'globex-admin@example.com',
  },
  {
    scope: 'PaymentService',
    environment: ENV,
    tenantId: 'Globex',
    key: 'Timeouts:HttpSeconds',
    value: '45',
    isSecret: false,
    modifiedUtc: daysAgo(11),
    modifiedBy: 'globex-admin@example.com',
  },
]

// ============================================================
// Audit history (2-4 rows per entry, most-recent-first)
// ============================================================

let _auditIdCounter = 1
function auditId(): string {
  return `demo-audit-${_auditIdCounter++}`
}

function auditRow(
  scope: string,
  environment: string,
  key: string,
  action: ConfigAuditAction,
  oldValue: string | null,
  newValue: string | null,
  isSecret: boolean,
  daysAgoN: number,
  modifiedBy: string,
  tenantId: string = '',
): ConfigAuditEntry {
  return {
    id: auditId(),
    scope,
    environment,
    tenantId,
    key,
    oldValue,
    newValue,
    isSecret,
    action,
    modifiedUtc: daysAgo(daysAgoN),
    modifiedBy,
  }
}

export const DEMO_AUDIT_HISTORY: ConfigAuditEntry[] = [
  // ConnectionStrings:Default — 3 entries
  auditRow('PaymentService', ENV, 'ConnectionStrings:Default', 'Insert', null, 'Server=db-old.payments.example.com;Port=5432;Database=payments;User Id=app;Password=init!', true, 20, 'platform-admin@example.com'),
  auditRow('PaymentService', ENV, 'ConnectionStrings:Default', 'Update', 'Server=db-old.payments.example.com;Port=5432;Database=payments;User Id=app;Password=init!', 'Server=db.payments.example.com;Port=5432;Database=payments;User Id=app;Password=s3cr3t!old', true, 10, 'payments-lead@example.com'),
  auditRow('PaymentService', ENV, 'ConnectionStrings:Default', 'Update', 'Server=db.payments.example.com;Port=5432;Database=payments;User Id=app;Password=s3cr3t!old', 'Server=db.payments.example.com;Port=5432;Database=payments;User Id=app;Password=s3cr3t!', true, 2, 'automation-bot@example.com'),

  // Stripe:ApiKey — 3 entries (spec example)
  auditRow('PaymentService', ENV, 'Stripe:ApiKey', 'Insert', null, 'sk_test_DEMO_original_key_placeholder', true, 14, 'platform-admin@example.com'),
  auditRow('PaymentService', ENV, 'Stripe:ApiKey', 'Update', 'sk_test_DEMO_original_key_placeholder', 'sk_test_DEMO_rotated_key_2024', true, 7, 'payments-lead@example.com'),
  auditRow('PaymentService', ENV, 'Stripe:ApiKey', 'Update', 'sk_test_DEMO_rotated_key_2024', 'sk_test_DEMO_51Ht2gCKZ6eY17rAtxDfQ3BbX', true, 2, 'automation-bot@example.com'),

  // Stripe:WebhookSecret — 2 entries
  auditRow('PaymentService', ENV, 'Stripe:WebhookSecret', 'Insert', null, 'whsec_initial_webhook_secret', true, 21, 'platform-admin@example.com'),
  auditRow('PaymentService', ENV, 'Stripe:WebhookSecret', 'Update', 'whsec_initial_webhook_secret', 'whsec_DEMO_REPLACE_ME_NOT_REAL', true, 7, 'payments-lead@example.com'),

  // Features:NewCheckout — 3 entries
  auditRow('PaymentService', ENV, 'Features:NewCheckout', 'Insert', null, 'false', false, 20, 'platform-admin@example.com'),
  auditRow('PaymentService', ENV, 'Features:NewCheckout', 'Update', 'false', 'false', false, 10, 'dev-self@example.com'),
  auditRow('PaymentService', ENV, 'Features:NewCheckout', 'Update', 'false', 'true', false, 5, 'payments-lead@example.com'),

  // Timeouts:HttpSeconds — 2 entries
  auditRow('PaymentService', ENV, 'Timeouts:HttpSeconds', 'Insert', null, '15', false, 28, 'platform-admin@example.com'),
  auditRow('PaymentService', ENV, 'Timeouts:HttpSeconds', 'Update', '15', '30', false, 14, 'platform-admin@example.com'),

  // EmailNotifications:ReplyTo — 2 entries
  auditRow('PaymentService', ENV, 'EmailNotifications:ReplyTo', 'Insert', null, 'support@example.com', false, 28, 'platform-admin@example.com'),
  auditRow('PaymentService', ENV, 'EmailNotifications:ReplyTo', 'Update', 'support@example.com', 'noreply@example.com', false, 14, 'platform-admin@example.com'),

  // Shared: Logging:LogLevel — 2 entries
  auditRow('Shared', ENV, 'Logging:LogLevel', 'Insert', null, 'Debug', false, 30, 'platform-admin@example.com'),
  auditRow('Shared', ENV, 'Logging:LogLevel', 'Update', 'Debug', 'Information', false, 20, 'platform-admin@example.com'),

  // Shared: MetricsEndpoint:Url — 2 entries
  auditRow('Shared', ENV, 'MetricsEndpoint:Url', 'Insert', null, 'https://otel-staging.example.com:4317', false, 25, 'platform-admin@example.com'),
  auditRow('Shared', ENV, 'MetricsEndpoint:Url', 'Update', 'https://otel-staging.example.com:4317', 'https://otel.example.com:4317', false, 18, 'platform-admin@example.com'),

  // Shared: RateLimits:RequestsPerSecond — 3 entries
  auditRow('Shared', ENV, 'RateLimits:RequestsPerSecond', 'Insert', null, '50', false, 25, 'platform-admin@example.com'),
  auditRow('Shared', ENV, 'RateLimits:RequestsPerSecond', 'Update', '50', '75', false, 16, 'dev-self@example.com'),
  auditRow('Shared', ENV, 'RateLimits:RequestsPerSecond', 'Update', '75', '100', false, 10, 'dev-self@example.com'),

  // Shared: EmailNotifications:SmtpHost — 2 entries
  auditRow('Shared', ENV, 'EmailNotifications:SmtpHost', 'Insert', null, 'smtp.mailhost.example.com', false, 30, 'platform-admin@example.com'),
  auditRow('Shared', ENV, 'EmailNotifications:SmtpHost', 'Update', 'smtp.mailhost.example.com', 'smtp.example.com', false, 22, 'platform-admin@example.com'),

  // Shared: EmailNotifications:SmtpPassword — 3 entries
  auditRow('Shared', ENV, 'EmailNotifications:SmtpPassword', 'Insert', null, 'InitialSmtp!Pass', true, 22, 'platform-admin@example.com'),
  auditRow('Shared', ENV, 'EmailNotifications:SmtpPassword', 'Update', 'InitialSmtp!Pass', 'RotatedSmtp@2024', true, 10, 'automation-bot@example.com'),
  auditRow('Shared', ENV, 'EmailNotifications:SmtpPassword', 'Update', 'RotatedSmtp@2024', 'Smtp@Passw0rd!2024', true, 3, 'automation-bot@example.com'),

  // Shared: EmailNotifications:FromAddress — 2 entries
  auditRow('Shared', ENV, 'EmailNotifications:FromAddress', 'Insert', null, 'no-reply@example.com', false, 28, 'platform-admin@example.com'),
  auditRow('Shared', ENV, 'EmailNotifications:FromAddress', 'Update', 'no-reply@example.com', 'noreply@example.com', false, 22, 'platform-admin@example.com'),

  // PlatformDefaults: Cache:DefaultTtlSeconds — 2 entries
  auditRow('PlatformDefaults', ENV, 'Cache:DefaultTtlSeconds', 'Insert', null, '60', false, 30, 'platform-admin@example.com'),
  auditRow('PlatformDefaults', ENV, 'Cache:DefaultTtlSeconds', 'Update', '60', '300', false, 28, 'platform-admin@example.com'),

  // PlatformDefaults: Cache:MaxItems — 2 entries
  auditRow('PlatformDefaults', ENV, 'Cache:MaxItems', 'Insert', null, '1000', false, 30, 'platform-admin@example.com'),
  auditRow('PlatformDefaults', ENV, 'Cache:MaxItems', 'Update', '1000', '10000', false, 28, 'platform-admin@example.com'),

  // PlatformDefaults: Logging:MinLevel — 2 entries
  auditRow('PlatformDefaults', ENV, 'Logging:MinLevel', 'Insert', null, 'Information', false, 30, 'platform-admin@example.com'),
  auditRow('PlatformDefaults', ENV, 'Logging:MinLevel', 'Update', 'Information', 'Warning', false, 25, 'platform-admin@example.com'),

  // PlatformDefaults: DiagnosticsEndpoint:Url — 2 entries
  auditRow('PlatformDefaults', ENV, 'DiagnosticsEndpoint:Url', 'Insert', null, 'https://diag-staging.example.com/health', false, 30, 'platform-admin@example.com'),
  auditRow('PlatformDefaults', ENV, 'DiagnosticsEndpoint:Url', 'Update', 'https://diag-staging.example.com/health', 'https://diag.example.com/health', false, 25, 'platform-admin@example.com'),

  // PlatformDefaults: Compliance:DataRetentionDays — 2 entries
  auditRow('PlatformDefaults', ENV, 'Compliance:DataRetentionDays', 'Insert', null, '30', false, 30, 'platform-admin@example.com'),
  auditRow('PlatformDefaults', ENV, 'Compliance:DataRetentionDays', 'Update', '30', '90', false, 30, 'platform-admin@example.com'),

  // Stripe:Payment:ApiKey — 2 entries (B48 tree view demo)
  auditRow('PaymentService', ENV, 'Stripe:Payment:ApiKey', 'Insert', null, 'sk_test_DEMO_payment_initial_key', true, 15, 'platform-admin@example.com'),
  auditRow('PaymentService', ENV, 'Stripe:Payment:ApiKey', 'Update', 'sk_test_DEMO_payment_initial_key', 'sk_test_DEMO_payment_51Ht2gCKZ6eY17rAt', true, 3, 'payments-lead@example.com'),

  // Twilio:Sms:AuthToken — 2 entries (B48 tree view demo)
  auditRow('PaymentService', ENV, 'Twilio:Sms:AuthToken', 'Insert', null, 'twilio_auth_token_initial', true, 20, 'platform-admin@example.com'),
  auditRow('PaymentService', ENV, 'Twilio:Sms:AuthToken', 'Update', 'twilio_auth_token_initial', 'twilio_auth_token_secret_value', true, 5, 'automation-bot@example.com'),

  // --- Acme tenant audit history (B58) ---
  auditRow('PaymentService', ENV, 'Stripe:ApiKey', 'Insert', null, 'sk_test_DEMO_acme_initial_key', true, 14, 'acme-admin@example.com', 'Acme'),
  auditRow('PaymentService', ENV, 'Stripe:ApiKey', 'Update', 'sk_test_DEMO_acme_initial_key', 'sk_test_DEMO_acme_51Ht2gCKZ6eY17rAtACME', true, 4, 'acme-admin@example.com', 'Acme'),

  auditRow('PaymentService', ENV, 'Stripe:WebhookSecret', 'Insert', null, 'whsec_acme_initial_secret', true, 14, 'acme-admin@example.com', 'Acme'),
  auditRow('PaymentService', ENV, 'Stripe:WebhookSecret', 'Update', 'whsec_acme_initial_secret', 'whsec_acme_9aM3kP7LqN2oR5tACME', true, 4, 'acme-admin@example.com', 'Acme'),

  // --- Globex tenant audit history (B58) ---
  auditRow('PaymentService', ENV, 'Stripe:ApiKey', 'Insert', null, 'sk_test_DEMO_globex_initial_key', true, 12, 'globex-admin@example.com', 'Globex'),
  auditRow('PaymentService', ENV, 'Stripe:ApiKey', 'Update', 'sk_test_DEMO_globex_initial_key', 'sk_test_DEMO_globex_51Ht2gCKZ6eY17rAtGLOBEX', true, 3, 'globex-admin@example.com', 'Globex'),

  auditRow('PaymentService', ENV, 'Stripe:WebhookSecret', 'Insert', null, 'whsec_globex_initial_secret', true, 12, 'globex-admin@example.com', 'Globex'),
  auditRow('PaymentService', ENV, 'Stripe:WebhookSecret', 'Update', 'whsec_globex_initial_secret', 'whsec_globex_9aM3kP7LqN2oR5tGLOBEX', true, 3, 'globex-admin@example.com', 'Globex'),

  // --- Deleted-entry audit trail (for the global Audit Log page) ---
  // Legacy:OldSetting was inserted then deleted — no current entry exists, but
  // its audit history must remain reachable via the global audit page.
  auditRow('PaymentService', ENV, 'Legacy:OldSetting', 'Insert', null, 'deprecated', false, 13, 'platform-admin@example.com'),
  auditRow('PaymentService', ENV, 'Legacy:OldSetting', 'Delete', 'deprecated', null, false, 11, 'platform-admin@example.com'),
]
