# Spytify+ landing page

The marketing / portfolio site for Spytify+, deployed to GitHub Pages at
`https://petrajthomas.github.io/spytify-plus/`.

Stack: Vite + React + TypeScript, Framer Motion for animation, Inter (Fontsource).

## Develop

```bash
cd web
npm install
npm run dev      # local dev server with hot reload
npm run build    # type-check + production build into dist/
npm run preview  # preview the production build
```

## Deploy

Pushing to `main` triggers `.github/workflows/pages.yml`, which builds this
folder and publishes `web/dist` to GitHub Pages. The Vite `base` is
`/spytify-plus/` (see `vite.config.ts`); update it if the repo is renamed.
