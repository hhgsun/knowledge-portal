import { broadcastResponseToMainFrame } from "@azure/msal-browser/redirect-bridge";

/**
 * This script runs inside the popup window after Azure AD redirects back.
 * It uses MSAL v5's redirect bridge to broadcast the auth response
 * back to the parent window via BroadcastChannel, then closes the popup.
 */
broadcastResponseToMainFrame().catch((err) => {
  console.error("Popup callback error:", err);
});
