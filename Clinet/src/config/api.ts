/**
 * Base URL for the SMS Web API (WebApplication1).
 * Override with VITE_API_BASE_URL in .env (HTTPS profile defaults to port 7056 in launchSettings.json).
 */
export const API_BASE_URL: string =
  (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '') ??
  'https://localhost:7056';

/**
 * SignalR hub URL (full path to /campaignHub on the API host).
 * Must match the backend origin (not the Vite dev server) when using credentials. Override with VITE_SIGNALR_HUB_URL.
 */
export const SIGNALR_HUB_URL: string =
  (import.meta.env.VITE_SIGNALR_HUB_URL as string | undefined)?.trim() ||
  'https://localhost:7056/campaignHub';
