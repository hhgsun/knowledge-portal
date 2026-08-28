# Knowledge Portal — Frontend

React SPA powering the Knowledge Portal user interface.

## Tech Stack

| Component | Version |
|-----------|---------|
| React | 19 |
| TypeScript | Strict mode |
| Vite | 8.x |
| React Router | v7 |
| Tailwind CSS | v4 |
| Editor | Milkdown Crepe (ProseMirror, CommonMark/GFM) |
| Icons | lucide-react |
| Notifications | sonner |

## Quick Start

```bash
cd frontend
npm install
npm run dev
```

Dev server starts on **http://localhost:5173**. API requests to `/api/*` are proxied to `http://localhost:5174`.

Set `VITE_ASSISTANT_ENABLED=false` before build/start to omit the Assistant sidebar item and `/assistant` route. The default is enabled; after authentication the UI also reads `/api/capabilities`, so the independent backend `Assistant:Enabled` kill switch hides the route without requiring a frontend rebuild.

When enabled by runtime capabilities, the Assistant page uses SSE for verified-answer streaming and provides session-owned multi-turn conversation history, new/delete/clear conversation actions, source citations, confidence details, semantic-cache indicators and feedback controls. Administrators review feedback-derived routing dataset candidates and shadow-routing summaries on the RAG evaluations page.

## Project Structure

```
src/
├── App.tsx              # Routes (ProtectedRoute + RoleRoute wrappers)
├── main.tsx             # Entry point
├── index.css            # Tailwind CSS imports
├── contexts/
│   └── AuthContext.tsx  # JWT auth state (localStorage)
├── hooks/
│   └── useApi.ts        # Fetch wrapper (auto-JWT, auto-logout on 401)
├── components/
│   ├── layout/          # AppShell, Sidebar, MCP connection modal
│   ├── editor/          # Milkdown editor and shared article form
│   ├── error-boundary.tsx
│   └── toast-provider.tsx
├── pages/               # Flat route page components
│   ├── LoginPage.tsx
│   ├── RegisterPage.tsx
│   ├── HomePage.tsx
│   ├── ArticlesPage.tsx
│   ├── NewArticlePage.tsx
│   ├── EditArticlePage.tsx
│   ├── ArticleViewPage.tsx
│   ├── SearchPage.tsx
│   ├── VersionsPage.tsx
│   ├── AnalyticsPage.tsx
│   ├── AdminUsersPage.tsx
│   ├── AdminApiKeysPage.tsx
│   └── NotFoundPage.tsx
└── lib/
    └── utils.ts         # cn() helper (clsx + tailwind-merge)
```

## Key Patterns

- **Auth**: JWT stored in localStorage, managed by `AuthContext`
- **API calls**: Always use `useApi` hook — auto-attaches Bearer token, triggers logout on 401
- **Notifications**: Use `toast.success()` / `toast.error()` from `sonner`
- **Routing**: `ProtectedRoute` for auth-required pages, `RoleRoute` for role-restricted pages
- **Styling**: Tailwind CSS utility-first, `cn()` helper for conditional classes

## Commands

| Task | Command |
|------|---------|
| Dev server | `npm run dev` |
| Build | `npm run build` |
| Lint | `npm run lint` |
| Preview build | `npm run preview` |
import reactDom from 'eslint-plugin-react-dom'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      // Other configs...
      // Enable lint rules for React
      reactX.configs['recommended-typescript'],
      // Enable lint rules for React DOM
      reactDom.configs.recommended,
    ],
    languageOptions: {
      parserOptions: {
        project: ['./tsconfig.node.json', './tsconfig.app.json'],
        tsconfigRootDir: import.meta.dirname,
      },
      // other options...
    },
  },
])
```
