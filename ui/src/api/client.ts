import axios from 'axios'

/**
 * Demo-mode detection.
 *
 * Activated by either:
 *   1. `?demo` query string in the URL  (runtime, lazy-loads demo chunk)
 *   2. Vite started with `--mode demo`  (build-time, `import.meta.env.MODE === 'demo'`)
 *
 * The `?demo` path triggers a dynamic import at runtime, so the demo data
 * is in a separate lazy chunk.  In a production build without `?demo`, the
 * chunk is never requested — effectively tree-shaken via code-splitting.
 *
 * For `--mode demo`, Vite sets `import.meta.env.MODE` to `'demo'` automatically;
 * no override in vite.config.ts is needed.
 */
export const isDemoMode: boolean = (() => {
  if (typeof window !== 'undefined') {
    const hasQuery = new URLSearchParams(window.location.search).has('demo')
    if (hasQuery) return true
  }
  return import.meta.env.MODE === 'demo'
})()

function resolveApiPrefix(): string {
  // 1. Build-time env var (Vite dev server / custom build).
  const envPrefix = import.meta.env.VITE_API_PREFIX as string | undefined
  if (envPrefix) {
    return envPrefix
  }

  // 2. Runtime meta tag injected by MapDbConfigUi when serving from the embedded assembly.
  const meta = document.querySelector<HTMLMetaElement>(
    'meta[name="db-config-api-prefix"]',
  )
  if (meta?.content) {
    return meta.content
  }

  return '/api/dbconfig'
}

const api = axios.create({
  baseURL: resolveApiPrefix(),
})

export default api
