import { createRoot } from "react-dom/client";
import { PublicClientApplication } from "@azure/msal-browser";
import { MsalProvider } from "@azure/msal-react";
import { msalConfig } from "./config/msalConfig";
import { AuthProvider } from "./contexts/AuthContext";
import { ThemeProvider } from "./contexts/ThemeContext";
import { ErrorBoundary } from "./components/error-boundary";
import { ToastProvider } from "./components/toast-provider";
import App from "./App";
import "./index.css";

const msalInstance = new PublicClientApplication(msalConfig);

msalInstance.initialize().then(() => {
  // Handle any pending redirect/popup response; ignore stale cache errors
  return msalInstance.handleRedirectPromise().catch(() => null);
}).then(() => {
  createRoot(document.getElementById("root")!).render(
    <ErrorBoundary>
      <MsalProvider instance={msalInstance}>
        <ThemeProvider>
          <AuthProvider>
            <App />
            <ToastProvider />
          </AuthProvider>
        </ThemeProvider>
      </MsalProvider>
    </ErrorBoundary>
  );
});
