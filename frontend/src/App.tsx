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
          <Route path="/analytics" element={<AnalyticsPage />} />
          <Route path="/admin/users" element={<AdminUsersPage />} />
          <Route path="/settings/keys" element={<SettingsKeysPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
