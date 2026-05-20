/**
 * Auth API surface for the built-in cookie login.
 *
 * Routed through the same `db-config-api-prefix` discovery used by other API
 * clients, but uses `fetch` directly so the call shape (JSON body, cookie
 * inclusion) is explicit and so demo mode can override the surface cleanly.
 *
 * Endpoints (when the host enables `UseBuiltInLogin<T>()`):
 *  - GET  {apiPrefix}/auth/status
 *  - POST {apiPrefix}/auth/login
 *  - POST {apiPrefix}/auth/logout
 *
 * `window.dbConfig.apiPrefix` (injected by EmbeddedStaticFileMiddleware) is the
 * source of truth for the prefix in production. Tests / dev fall back to the
 * meta tag (`db-config-api-prefix`).
 */

import { isDemoMode } from './client'

export interface AuthStatus {
  authenticated: boolean
  hasBuiltInLogin: boolean
  username?: string | null
}

export interface LoginResult {
  ok: boolean
  error?: string
}

declare global {
  interface Window {
    dbConfig?: { apiPrefix: string; hasBuiltInLogin: boolean }
  }
}

function resolveAuthBase(): string {
  if (typeof window !== 'undefined' && window.dbConfig?.apiPrefix) {
    return window.dbConfig.apiPrefix
  }
  const meta = typeof document !== 'undefined'
    ? document.querySelector<HTMLMetaElement>('meta[name="db-config-api-prefix"]')
    : null
  if (meta?.content) {
    return meta.content
  }

  return '/api/dbconfig'
}

// ---------------------------------------------------------------------------
// Demo-mode short-circuits
// ---------------------------------------------------------------------------
// In demo mode the SPA does not hit the backend. By default the user is
// "authenticated" so the dashboard renders without a sign-in step. Appending
// `?demoLoggedOut` to the URL flips the state — screenshot tests use this to
// capture the LoginPage from the real React component.

function isDemoLoggedOut(): boolean {
  if (typeof window === 'undefined') {
    return false
  }

  return new URLSearchParams(window.location.search).has('demoLoggedOut')
}

let demoSignedIn = !isDemoLoggedOut()

// ---------------------------------------------------------------------------
// Public surface
// ---------------------------------------------------------------------------

export async function fetchAuthStatus(): Promise<AuthStatus> {
  if (isDemoMode) {
    return {
      authenticated: demoSignedIn,
      hasBuiltInLogin: true,
      username: demoSignedIn ? 'demo-user' : null,
    }
  }
  if (typeof window === 'undefined' || !window.dbConfig?.hasBuiltInLogin) {
    // No built-in login wired by the host — treat as authenticated so the SPA
    // mounts immediately. Host-managed auth handles failures separately.
    return { authenticated: true, hasBuiltInLogin: false }
  }
  const base = resolveAuthBase()
  try {
    const res = await fetch(`${base}/auth/status`, { credentials: 'include' })
    if (!res.ok) {
      return { authenticated: false, hasBuiltInLogin: true }
    }
    const data = (await res.json()) as AuthStatus

    return data
  } catch {
    return { authenticated: false, hasBuiltInLogin: true }
  }
}

export async function login(username: string, password: string): Promise<LoginResult> {
  if (isDemoMode) {
    demoSignedIn = true

    return { ok: true }
  }
  const base = resolveAuthBase()
  try {
    const res = await fetch(`${base}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
      credentials: 'include',
    })
    if (res.ok) {
      return { ok: true }
    }
    const data = await res.json().catch(() => null) as { error?: string } | null

    return { ok: false, error: data?.error ?? 'Invalid credentials' }
  } catch {
    return { ok: false, error: 'Network error' }
  }
}

export async function logout(): Promise<void> {
  if (isDemoMode) {
    demoSignedIn = false

    return
  }
  const base = resolveAuthBase()
  try {
    await fetch(`${base}/auth/logout`, {
      method: 'POST',
      credentials: 'include',
    })
  } catch {
    // Swallow — the UI re-checks status afterwards and will route accordingly.
  }
}
