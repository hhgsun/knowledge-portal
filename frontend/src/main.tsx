import { createRoot } from "react-dom/client";
import { AuthProvider } from "./contexts/AuthContext";
import { ErrorBoundary } from "./components/error-boundary";
import { ToastProvider } from "./components/toast-provider";
import App from "./App";
import "./index.css";

createRoot(document.getElementById("root")!).render(
  <ErrorBoundary>
    <AuthProvider>
      <App />
      <ToastProvider />
    </AuthProvider>
  </ErrorBoundary>
);
