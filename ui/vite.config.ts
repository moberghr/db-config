import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'

// Demo mode is activated by starting Vite with `--mode demo`.
// Vite automatically sets import.meta.env.MODE to the supplied mode value,
// so no extra `define` override is needed here.
// At runtime the `?demo` query string can also activate demo mode (see client.ts).

export default defineConfig({
  plugins: [react(), tailwindcss()],
  base: './',
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  build: {
    // Write directly into the DbConfig.Ui project so MSBuild's static EmbeddedResource
    // glob picks the bundle up at project-load time. Mirrors Warp's pattern.
    outDir: '../src/core/DbConfig.Ui/dist',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5000',
    },
  },
})
