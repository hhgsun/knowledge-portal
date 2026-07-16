import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'path'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')

  return {
    plugins: [react(), tailwindcss()],
    build: {
      rollupOptions: {
        input: {
          main: resolve(__dirname, 'index.html'),
          'auth-popup-callback': resolve(__dirname, 'auth-popup-callback.html'),
        },
      },
    },
    server: {
      port: 5173,
      proxy: {
        '/api': {
          target: env.VITE_API_BASE_URL || 'http://localhost:5174',
          changeOrigin: true,
        },
        '/mcp': {
          target: env.VITE_API_BASE_URL || 'http://localhost:5174',
          changeOrigin: true,
        },
      },
    },
  }
})
