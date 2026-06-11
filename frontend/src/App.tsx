import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import { useAuth } from "./contexts/AuthContext";
import { AppShell } from "./components/layout/app-shell";

import LoginPage from "./pages/LoginPage";
import RegisterPage from "./pages/RegisterPage";
import HomePage from "./pages/HomePage";
import ArticlesPage from "./pages/ArticlesPage";
import ArticleViewPage from "./pages/ArticleViewPage";
import EditArticlePage from "./pages/EditArticlePage";
import NewArticlePage from "./pages/NewArticlePage";
import VersionsPage from "./pages/VersionsPage";
import SearchPage from "./pages/SearchPage";
import AnalyticsPage from "./pages/AnalyticsPage";
import AdminUsersPage from "./pages/AdminUsersPage";
import SettingsKeysPage from "./pages/SettingsKeysPage";
import TagsPage from "./pages/TagsPage";
import NotFoundPage from "./pages/NotFoundPage";
import ProfilePage from "./pages/ProfilePage";
import LookupsPage from "./pages/LookupsPage";

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { user, loading } = useAuth();

  if (loading) {
    return <div className="flex items-center justify-center min-h-screen min-w-screen text-zinc-500">Loading...</div>;
  }

  if (!user) {
    return <Navigate to="/login" replace />;
  }

  return <>{children}</>;
}

function RoleRoute({ children, roles }: { children: React.ReactNode; roles: string[] }) {
  const { user } = useAuth();

  if (!user || !roles.includes(user.role)) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        {/* Auth pages — no shell */}
        <Route path="/login" element={<AppShell />}>
          <Route index element={<LoginPage />} />
        </Route>
        <Route path="/register" element={<AppShell />}>
          <Route index element={<RegisterPage />} />
        </Route>

        {/* Protected pages with shell */}
        <Route
          element={
            <ProtectedRoute>
              <AppShell />
            </ProtectedRoute>
          }
        >
          <Route path="/" element={<HomePage />} />
          <Route path="/articles" element={<ArticlesPage />} />
          <Route path="/articles/new" element={<NewArticlePage />} />
          <Route path="/articles/:slug" element={<ArticleViewPage />} />
          <Route path="/articles/:slug/edit" element={<EditArticlePage />} />
          <Route path="/articles/:slug/versions" element={<VersionsPage />} />
          <Route path="/search" element={<SearchPage />} />
          <Route path="/profile" element={<ProfilePage />} />
          <Route path="/analytics" element={<RoleRoute roles={["admin", "editor"]}><AnalyticsPage /></RoleRoute>} />
          <Route path="/tags" element={<RoleRoute roles={["admin", "editor"]}><TagsPage /></RoleRoute>} />
          <Route path="/admin/users" element={<RoleRoute roles={["admin"]}><AdminUsersPage /></RoleRoute>} />
          <Route path="/settings/keys" element={<RoleRoute roles={["admin"]}><SettingsKeysPage /></RoleRoute>} />
          <Route path="/settings/lookups" element={<RoleRoute roles={["admin", "editor"]}><LookupsPage /></RoleRoute>} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
