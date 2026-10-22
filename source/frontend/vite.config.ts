import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'
import { fileURLToPath, URL } from 'node:url'

// Dev server proxies API + SignalR to the backend so the browser stays same-origin (no CORS needed locally).
const apiTarget = process.env.VITE_PROXY_TARGET ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
      '/hubs': { target: apiTarget, changeOrigin: true, ws: true },
      '/health': { target: apiTarget, changeOrigin: true },
    },
  },
  build: {
    chunkSizeWarningLimit: 1200,
  },
})
