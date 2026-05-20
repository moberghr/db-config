/**
 * Demo-mode entry point.
 *
 * Called once at app startup (in main.tsx) when demo mode is active.
 * Pre-seeds the scope store so the UI shows data immediately without
 * the user having to type an Scope/Environment.
 */

import { useScopeStore } from '@/store/scopeStore'

export function setupDemo(): void {
  // Only seed if the scope is empty (fresh session or first visit).
  const { scope, environment } = useScopeStore.getState()
  if (!scope || !environment) {
    useScopeStore.getState().setScope('PaymentService', 'Production')
    useScopeStore.getState().setIncludeScopes(['Shared', 'PlatformDefaults'])
  }
}
