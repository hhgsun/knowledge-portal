import { Link, useLocation } from "react-router-dom";
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
  ScrollText,
  Stethoscope,
  Cpu,
  Star,
  ExternalLink,
  ArrowUpDown,
  Files,
  FlaskConical,
  Bot,
} from "lucide-react";
import { cn } from "../../lib/utils";
import { useState, useEffect } from "react";
import { useAuth } from "../../contexts/AuthContext";
import { useTheme } from "../../contexts/ThemeContext";
import { McpModal } from "./mcp-modal";
import { useFeaturedLinks, resolveFeaturedLinkHref } from "../../hooks/useFeaturedLinks";
import { getColorClasses, getIconComponent } from "../../lib/lookup-utils";
import type { FeaturedLink } from "../../types/api";
import { assistantEnabled } from "../../config/features";

interface NavItem {
  label: string;
  href: string;
  icon: React.ReactNode;
  children?: NavItem[];
}

const navigation: NavItem[] = [
  { label: "Home", href: "/", icon: <Home size={18} /> },
  ...(assistantEnabled
    ? [{ label: "Assistant", href: "/assistant", icon: <Bot size={18} /> }]
    : []),
  { label: "Search", href: "/search", icon: <Search size={18} /> },
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
      { label: "Bulk Knowledge Import", href: "/articles/import", icon: <Files size={16} /> },
    ],
  },
  { label: "Analytics", href: "/analytics", icon: <BarChart3 size={18} /> },
];

const adminNavigation: NavItem[] = [
  { label: "Import / Export", href: "/settings/bulk-transfer", icon: <ArrowUpDown size={18} /> },
  { label: "Tags", href: "/tags", icon: <Tag size={18} /> },
  { label: "Lookups", href: "/settings/lookups", icon: <Settings2 size={18} /> },
  { label: "Featured Links", href: "/settings/featured-links", icon: <Star size={18} /> },
  { label: "Users", href: "/admin/users", icon: <Users size={18} /> },
  { label: "API Keys", href: "/admin/keys", icon: <Key size={18} /> },
  { label: "Logs", href: "/settings/logs", icon: <ScrollText size={18} /> },
  { label: "Search Health", href: "/settings/search", icon: <Stethoscope size={18} /> },
  { label: "RAG Quality", href: "/settings/rag-evaluations", icon: <FlaskConical size={18} /> },
];

function FeaturedNavLink({ link, collapsed }: { link: FeaturedLink; collapsed: boolean }) {
  const { pathname, search } = useLocation();
  const { href, external } = resolveFeaturedLinkHref(link);
  const IconComp = getIconComponent(link.icon || "star");
  const iconClass = link.color ? getColorClasses(link.color).text : undefined;
  const isActive = !external && pathname + search === href;

  const className = cn(
    "flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition-colors",
    isActive
      ? "bg-zinc-100 text-zinc-900 font-medium dark:bg-zinc-800 dark:text-zinc-100"
      : "text-zinc-600 hover:bg-zinc-50 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800/50 dark:hover:text-zinc-100",
    collapsed && "justify-center px-2"
  );

  if (external) {
    return (
      <a
        href={href}
        target="_blank"
        rel="noopener noreferrer"
        title={collapsed ? link.label : undefined}
        aria-label={link.label}
        className={className}
      >
        <span aria-hidden="true"><IconComp size={18} className={iconClass} /></span>
        {!collapsed && (
          <span className="flex items-center gap-1.5 min-w-0">
            <span className="truncate">{link.label}</span>
            <ExternalLink size={12} className="shrink-0 text-zinc-400" aria-hidden="true" />
          </span>
        )}
      </a>
    );
  }

  return (
    <Link
      to={href}
      title={collapsed ? link.label : undefined}
      aria-label={link.label}
      aria-current={isActive ? "page" : undefined}
      className={className}
    >
      <span aria-hidden="true"><IconComp size={18} className={iconClass} /></span>
      {!collapsed && <span className="truncate">{link.label}</span>}
    </Link>
  );
}

function NavLink({ item, depth = 0, collapsed = false }: { item: NavItem; depth?: number; collapsed?: boolean }) {
  const { pathname } = useLocation();
  const isActive =
    pathname === item.href ||
    (item.href !== "/" && pathname.startsWith(item.href));
  const [expanded, setExpanded] = useState(isActive);

  return (
    <div role="none">
      <div className="flex items-center">
        <Link
          to={item.href}
          title={collapsed ? item.label : undefined}
          aria-label={item.label}
          aria-current={isActive ? "page" : undefined}
          className={cn(
            "flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition-colors flex-1",
            isActive
              ? "bg-zinc-100 text-zinc-900 font-medium dark:bg-zinc-800 dark:text-zinc-100"
              : "text-zinc-600 hover:bg-zinc-50 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800/50 dark:hover:text-zinc-100",
            depth > 0 && "ml-4 text-xs",
            collapsed && "justify-center px-2"
          )}
        >
          <span aria-hidden="true">{item.icon}</span>
          {!collapsed && item.label}
        </Link>
        {item.children && !collapsed && (
          <button
            onClick={() => setExpanded(!expanded)}
            aria-expanded={expanded}
            aria-label={`${expanded ? "Collapse" : "Expand"} ${item.label} submenu`}
            className="p-1 rounded hover:bg-zinc-100 dark:hover:bg-zinc-800"
          >
            {expanded ? (
              <ChevronDown size={14} aria-hidden="true" />
            ) : (
              <ChevronRight size={14} aria-hidden="true" />
            )}
          </button>
        )}
      </div>
      {item.children && expanded && !collapsed && (
        <div className="mt-1" role="group" aria-label={`${item.label} submenu`}>
          {item.children.map((child) => (
            <NavLink key={child.href} item={child} depth={depth + 1} collapsed={collapsed} />
          ))}
        </div>
      )}
    </div>
  );
}

export function Sidebar() {
  const { user } = useAuth();
  const { theme, setTheme } = useTheme();
  const { pathname } = useLocation();
  const role = user?.role;
  const isAdmin = role === "admin";
  const isEditorOrAdmin = role === "admin" || role === "editor";
  const [collapsed, setCollapsed] = useState(false);
  const [isHovered, setIsHovered] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [mcpModalOpen, setMcpModalOpen] = useState(false);
  const { links: featuredLinks } = useFeaturedLinks();

  // Close mobile sidebar on route change
  useEffect(() => {
    setMobileOpen(false);
  }, [pathname]);

  // Close mobile sidebar on Escape key
  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === "Escape") setMobileOpen(false);
    };
    if (mobileOpen) {
      document.addEventListener("keydown", handleEscape);
      return () => document.removeEventListener("keydown", handleEscape);
    }
  }, [mobileOpen]);

  const sidebarContent = (
    <>
      {/* Logo + Toggle */}
      <div className="flex items-center justify-between p-4 border-b border-zinc-200 dark:border-zinc-800">
        <Link
          to="/"
          aria-label="Go to homepage"
          className="flex items-center gap-1 overflow-hidden select-none my-1"
        >
          <BookSearch size={24} className="text-blue-600 shrink-0 ml-1" />
          <span className={'font-bold text-md whitespace-nowrap overflow-hidden ' + (!collapsed ? 'block' : 'hidden')}>
            <span>Knowledge</span>
            <span className="text-blue-600">Portal</span>
          </span>
        </Link>
        {/* Desktop: collapse/expand toggle */}
        <button
          onClick={() => setCollapsed(!collapsed)}
          title={collapsed ? "Expand sidebar" : "Collapse sidebar"}
          className={cn("p-1.5 rounded-lg text-zinc-500 hover:bg-zinc-100 hover:text-zinc-900 dark:hover:bg-zinc-800 dark:hover:text-zinc-100 transition-colors shrink-0 hidden lg:block",
            (!collapsed || (collapsed && isHovered)) ? "lg:block" : "lg:hidden")
          }
        >
          {collapsed ? <PanelLeftOpen size={18} /> : <PanelLeftClose size={18} />}
        </button>
        {/* Mobile: close button */}
        <button
          onClick={() => setMobileOpen(false)}
          className="p-1.5 rounded-lg text-zinc-500 hover:bg-zinc-100 hover:text-zinc-900 dark:hover:bg-zinc-800 dark:hover:text-zinc-100 transition-colors shrink-0 lg:hidden"
        >
          <PanelLeftClose size={18} />
        </button>
      </div>

      {/* Navigation */}
      <nav aria-label="Main navigation" className="sidebar-scrollbar flex-1 px-3 py-4 space-y-1 overflow-y-auto">
        <div className="space-y-1">
          {navigation
            .filter((item) => item.href !== "/analytics" || isEditorOrAdmin)
            .map((item) => (
              <NavLink key={item.href} item={item} collapsed={collapsed} />
            ))}
        </div>

        {/* Featured Links */}
        {featuredLinks.length > 0 && (
          <div className="pt-6">
            {!collapsed && (
              <p className="px-3 text-xs font-semibold text-zinc-400 uppercase tracking-wider mb-2">
                Seçkinler
              </p>
            )}
            <div className="space-y-1">
              {featuredLinks.map((link) => (
                <FeaturedNavLink key={link.id} link={link} collapsed={collapsed} />
              ))}
            </div>
          </div>
        )}

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
                  if (item.href === "/admin/keys") return isAdmin;
                  if (item.href === "/settings/logs") return isAdmin;
                  if (item.href === "/settings/search") return isAdmin;
                  if (item.href === "/settings/rag-evaluations") return isAdmin;
                  if (item.href === "/tags") return isEditorOrAdmin;
                  if (item.href === "/settings/lookups") return isEditorOrAdmin;
                  if (item.href === "/settings/featured-links") return isAdmin;
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
            <div className="w-6 h-6 rounded-full bg-blue-100 dark:bg-blue-900/30 flex items-center justify-center text-[10px] font-semibold text-blue-700 dark:text-blue-300 shrink-0">
              {user?.name ? user.name.split(' ').map(w => w[0]).join('').toUpperCase().slice(0, 2) : '?'}
            </div>
            {!collapsed && <span className="truncate max-w-[100px]">{user?.name ?? "Profile"}</span>}
          </Link>
          <button
            onClick={() => setMcpModalOpen(true)}
            title="MCP Bağlantı Bilgileri"
            className={cn(
              "p-2 rounded-lg text-zinc-500 hover:bg-zinc-100 hover:text-zinc-900 dark:hover:bg-zinc-800 dark:hover:text-zinc-100 transition-colors shrink-0",
              collapsed && "hidden"
            )}
          >
            <Cpu size={16} />
          </button>
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
    </>
  );

  return (
    <>
      {/* Mobile: hamburger button (visible when sidebar is closed) */}
      {!mobileOpen && (
        <button
          onClick={() => setMobileOpen(true)}
          aria-label="Open navigation menu"
          aria-expanded={mobileOpen}
          className="fixed top-3 left-3 z-40 p-2 rounded-lg bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-800 text-zinc-600 dark:text-zinc-400 shadow-sm hover:bg-zinc-50 dark:hover:bg-zinc-800 transition-colors lg:hidden"
          title="Open menu"
        >
          <PanelLeftOpen size={18} aria-hidden="true" />
        </button>
      )}

      {/* Mobile: backdrop overlay */}
      {mobileOpen && (
        <div
          className="fixed inset-0 z-40 bg-black/40 lg:hidden"
          onClick={() => setMobileOpen(false)}
          aria-hidden="true"
        />
      )}

      {/* Mobile: sidebar overlay */}
      <aside
        aria-label="Mobile navigation"
        aria-hidden={!mobileOpen}
        className={cn(
          "fixed inset-y-0 left-0 z-50 flex flex-col w-64 bg-white dark:bg-zinc-950 border-r border-zinc-200 dark:border-zinc-800 transition-transform duration-300 lg:hidden",
          mobileOpen ? "translate-x-0" : "-translate-x-full"
        )}
      >
        {sidebarContent}
      </aside>

      {/* Desktop: static sidebar */}
      <aside
        aria-label="Main navigation"
        onMouseEnter={() => setIsHovered(true)}
        onMouseLeave={() => setIsHovered(false)}
        className={cn(
          "hidden lg:flex lg:flex-col lg:border-r border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 h-screen sticky top-0 transition-all duration-300",
          collapsed ? "lg:w-16" : "lg:w-64"
        )}
      >
        {sidebarContent}
      </aside>

      <McpModal open={mcpModalOpen} onClose={() => setMcpModalOpen(false)} />
    </>
  );
}
