import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
// import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    // vueDevTools(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    },
  },
  server: {
    proxy: {
      // 你前端呼叫 /api/* → 轉發到 SharedApi
      '/api': {
        target: 'https://localhost:7103', // ← 換成你的 SharedApi 埠
        changeOrigin: true,
        secure: false, // 本機自簽憑證用 false；若後端是 http 就可移除
      },
    },
  }
});
