import { Link, useLocation, useNavigate } from "react-router-dom";
import {
  BookOpen,
  Search,
  BarChart3,
  Users,
  PlusCircle,
  Home,
  ChevronDown,
  ChevronRight,
  Key,
  User,
  LogOut,
  BookSearch,
  PanelLeftClose,
  PanelLeftOpen,
  Heart,
  Copyright,
  Tag,
  Sun,
  Moon,
  Monitor,
  Settings2,
} from "lucide-react";
import { cn } from "../../lib/utils";
import { useState } from "react";
import { useAuth } from "../../contexts/AuthContext";
import { useTheme } from "../../contexts/ThemeContext";

interface NavItem {
  label: string;
  href: string;
  icon: React.ReactNode;
  children?: NavItem[];
}

const navigation: NavItem[] = [
  { label: "Home", href: "/", icon: <Home size={18} /> },
  {
    label: "Articles",
    href: "/articles",
    icon: <BookOpen size={18} />,
    children: [
      {
        label: "New Article",
        href: "/articles/new",
        icon: <PlusCircle size={16} />,
      },
    ],
  },
  { label: "Search", href: "/search", icon: <Search size={18} /> },
  { label: "Analytics", href: "/analytics", icon: <BarChart3 size={18} /> },
];

const adminNavigation: NavItem[] = [
  { label: "Tags", href: "/tags", icon: <Tag size={18} /> },
  { label: "Lookups", href: "/settings/lookups", icon: <Settings2 size={18} /> },
  { label: "Users", href: "/admin/users", icon: <Users size={18} /> },
  { label: "API Keys", href: "/settings/keys", icon: <Key size={18} /> },
];

function NavLink({ item, depth = 0, collapsed = false }: { item: NavItem; depth?: number; collapsed?: boolean }) {
  const { pathname } = useLocation();
  const isActive =
    pathname === item.href ||
    (item.href !== "/" && pathname.startsWith(item.href));
  const [expanded, setExpanded] = useState(isActive);

  return (
    <div>
      <div className="flex items-center">
        <Link
          to={item.href}
          title={collapsed ? item.label : undefined}
          className={cn(
            "flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition-colors flex-1",
            isActive
              ? "bg-zinc-100 text-zinc-900 font-medium dark:bg-zinc-800 dark:text-zinc-100"
              : "text-zinc-600 hover:bg-zinc-50 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800/50 dark:hover:text-zinc-100",
            depth > 0 && "ml-4 text-xs",
            collapsed && "justify-center px-2"
          )}
        >
          {item.icon}
          {!collapsed && item.label}
        </Link>
        {item.children && !collapsed && (
          <button
            onClick={() => setExpanded(!expanded)}
            className="p-1 rounded hover:bg-zinc-100 dark:hover:bg-zinc-800"
          >
            {expanded ? (
              <ChevronDown size={14} />
            ) : (
              <ChevronRight size={14} />
            )}
          </button>
        )}
      </div>
      {item.children && expanded && !collapsed && (
        <div className="mt-1">
          {item.children.map((child) => (
            <NavLink key={child.href} item={child} depth={depth + 1} collapsed={collapsed} />
          ))}
        </div>
      )}
    </div>
  );
}

export function Sidebar() {
  const { user, logout } = useAuth();
  const { theme, setTheme } = useTheme();
  const navigate = useNavigate();
  const role = user?.role;
  const isAdmin = role === "admin";
  const isEditorOrAdmin = role === "admin" || role === "editor";
  const [collapsed, setCollapsed] = useState(false);
  const [isHovered, setIsHovered] = useState(false);

  return (
    <aside
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      className={cn(
        "hidden lg:flex lg:flex-col lg:border-r border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 h-screen sticky top-0 transition-all duration-300",
        collapsed ? "lg:w-16" : "lg:w-64"
      )}>
      {/* Logo + Toggle */}
      <div className="flex items-center justify-between p-4 border-b border-zinc-200 dark:border-zinc-800">
        <div className="flex items-center gap-1 overflow-hidden select-none my-1">
          <BookSearch size={24} className="text-blue-600 shrink-0 ml-1" />
          <span className={'font-bold text-md whitespace-nowrap overflow-hidden ' + (!collapsed ? 'block' : 'hidden')}>
            <span>Knowledge</span>
            <span className="text-blue-600">Portal</span>
          </span>
        </div>
        <button
          onClick={() => setCollapsed(!collapsed)}
          title={collapsed ? "Expand sidebar" : "Collapse sidebar"}
          className={cn("p-1.5 rounded-lg text-zinc-500 hover:bg-zinc-100 hover:text-zinc-900 dark:hover:bg-zinc-800 dark:hover:text-zinc-100 transition-colors shrink-0",
            (!collapsed || (collapsed && isHovered)) ? "block" : "hidden")
          }
        >
          {collapsed ? <PanelLeftOpen size={18} /> : <PanelLeftClose size={18} />}
        </button>
      </div>

      {/* Navigation */}
      <nav className="flex-1 px-3 py-4 space-y-1 overflow-y-auto">
        <div className="space-y-1">
          {navigation.map((item) => (
            <NavLink key={item.href} item={item} collapsed={collapsed} />
          ))}
        </div>

        {/* Admin Section */}
        {isEditorOrAdmin && (
          <div className="pt-6">
            {!collapsed && (
              <p className="px-3 text-xs font-semibold text-zinc-400 uppercase tracking-wider mb-2">
                Admin
              </p>
            )}
            <div className="space-y-1">
              {adminNavigation
                .filter((item) => {
                  if (item.href === "/admin/users") return isAdmin;
                  if (item.href === "/settings/keys") return isAdmin;
                  if (item.href === "/tags") return isEditorOrAdmin;
                  if (item.href === "/settings/lookups") return isEditorOrAdmin;
                  return true;
                })
                .map((item) => (
                  <NavLink key={item.href} item={item} collapsed={collapsed} />
                ))}
            </div>
          </div>
        )}
      </nav>

      {/* Footer */}
      <div className="px-3 py-3 border-t border-b border-zinc-200 dark:border-zinc-800 space-y-1">
        <div className="flex items-center gap-1">
          <Link
            to="/profile"
            title={collapsed ? (user?.name ?? "Profile") : undefined}
            className={cn(
              "flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-zinc-600 hover:bg-zinc-50 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800/50 dark:hover:text-zinc-100 transition-colors flex-1",
              collapsed && "justify-center px-2"
            )}
          >
            <User size={18} />
            {!collapsed && <span className="truncate">{user?.name ?? "Profile"}</span>}
          </Link>
          <button
            onClick={() => setTheme(theme === "light" ? "dark" : theme === "dark" ? "system" : "light")}
            title={`Theme: ${theme}`}
            className={cn(
              "p-2 rounded-lg text-zinc-500 hover:bg-zinc-100 hover:text-zinc-900 dark:hover:bg-zinc-800 dark:hover:text-zinc-100 transition-colors shrink-0",
              collapsed && "hidden"
            )}
          >
            {theme === "light" ? <Sun size={16} /> : theme === "dark" ? <Moon size={16} /> : <Monitor size={16} />}
          </button>
        </div>
        <button
          onClick={() => { logout(); navigate("/login"); }}
          title={collapsed ? "Sign out" : undefined}
          className={cn(
            "flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-zinc-600 hover:bg-zinc-50 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800/50 dark:hover:text-zinc-100 transition-colors w-full",
            collapsed && "justify-center px-2"
          )}
        >
          <LogOut size={18} />
          {!collapsed && "Sign out"}
        </button>
      </div>

      {/* Copyright */}
      <div className="px-3 py-2 flex items-center justify-center gap-1 text-[11px] text-zinc-400 dark:text-zinc-600">
        {!collapsed && <span className="flex items-center gap-1">
          <Copyright size={12} />
          {new Date().getFullYear()}
          <a href="https://finagotech.com.tr" target="_blank" rel="noopener noreferrer">
            FinagoTech
          </a>
        </span>}
        <Heart size={14} />
        {!collapsed && <span>DWH</span>}
      </div>
    </aside>
  );
}
