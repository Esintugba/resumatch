/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** API origin for a deployed build, e.g. https://api.example.com. Empty in development, where Vite proxies /api and /health. */
  readonly VITE_API_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
