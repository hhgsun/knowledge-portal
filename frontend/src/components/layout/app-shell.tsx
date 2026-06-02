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
      <div className="flex-1 flex flex-col min-h-screen">
        <main className="flex-1 p-6">
          <Outlet />
        </main>
      </div>
    </>
  );
}
