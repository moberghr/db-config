import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/globals.css'
import App from './App.tsx'
import { isDemoMode } from './api/client'
import { initTheme } from '@/store/themeStore'

initTheme()

async function bootstrap() {
  if (isDemoMode) {
    // Dynamically import so the demo code stays in a separate lazy chunk.
    const { setupDemo } = await import('./demo/index')
    setupDemo()
  }

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  )
}

void bootstrap()
