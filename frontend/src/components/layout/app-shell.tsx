import { useLocation, Outlet } from "react-router-dom";
import { Sidebar } from "./sidebar";

const AUTH_ROUTES = ["/login", "/register"];

export function AppShell() {
  const { pathname } = useLocation();
  const isAuthPage = AUTH_ROUTES.includes(pathname);

  if (isAuthPage) {
    return <Outlet />;
  }

  return (
    <>
      <Sidebar />
      <main className="flex-1 p-6 pt-14 lg:pt-6 min-h-screen overflow-y-auto">
        <Outlet />
      </main>
    </>
  );
}
