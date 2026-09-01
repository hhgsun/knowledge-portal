import { useLocation, Outlet } from "react-router-dom";
import { Sidebar } from "./sidebar";

const AUTH_ROUTES = ["/login", "/register"];

export function AppShell() {
  const { pathname } = useLocation();
  const isAuthPage = AUTH_ROUTES.includes(pathname);
  const isAssistantPage = pathname === "/assistant";

  if (isAuthPage) {
    return (
      <main id="main-content" role="main" aria-label="Authentication" className="w-full">
        <Outlet />
      </main>
    );
  }

  return (
    <div className="flex w-full">
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-[200] focus:px-4 focus:py-2 focus:bg-blue-600 focus:text-white focus:rounded-lg focus:text-sm"
      >
        Skip to main content
      </a>
      <Sidebar />
      <main
        id="main-content"
        role="main"
        aria-label="Page content"
        className={isAssistantPage
          ? "h-screen min-w-0 flex-1 overflow-hidden pt-14 lg:pt-0"
          : "min-h-screen flex-1 overflow-y-auto p-6 pt-14 lg:pt-6"}
      >
        <Outlet />
      </main>
    </div>
  );
}
