import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// GitHub Pages serves this project site under /spytify-plus/, so assets must be
// emitted relative to that base or they 404. Change this if the repo is renamed.
export default defineConfig({
  base: '/spytify-plus/',
  plugins: [react()],
})
