# web

UI of SecurityCheck Portal: React Router 8 in SPA mode (`ssr: false` in `react-router.config.ts`) with Tailwind 4. The production build is a static site (`build/client`) that the ASP.NET Core API serves from `api/wwwroot`, so UI and `/api` share one origin.

## Commands

Run from `web/` (after `npm install`):

- `npm run dev` - Vite dev server on `http://localhost:5173`. `/api` is proxied to Kestrel at `http://localhost:5143` (`vite.config.ts`), so start the API too: `dotnet run --project api --launch-profile http`.
- `npm run build` - SPA build into `build/client`.
- `npm run build:api` - `react-router build`, then `scripts/copy-to-api.mjs` replaces `../api/wwwroot` (gitignored) with `build/client`. After this the API alone serves UI and `/api`.
- `npm run typecheck` - `react-router typegen && tsc`. Run it after adding or renaming routes.

`npm start` (`react-router-serve`) and the `Dockerfile` come from the React Router template. Neither is part of the deployment flow described above, which uses `build:api`.

## UI conventions

- Design tokens (colours, light and `.dark` values) are in `app/app.css`; shared components are in `app/components/`.
- Open `/styleguide` under `npm run dev` to review tokens, contrast (WCAG AA) and components in light and dark theme. The route is not registered in a production build.
- UI text is Polish. Full rules for contributors and agents: the "UI Conventions" section in [`../AGENTS.md`](../AGENTS.md).
- `.agents/skills/react-router/` holds the React Router reference skill for agents.
