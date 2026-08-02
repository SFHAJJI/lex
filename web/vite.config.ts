import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// One bundle, emitted into the ASP.NET app's wwwroot. No router and no SSR: every
// permalink stays a server-rendered document (that is the crawlable surface); only the
// landing workspace is a component tree.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../src/Lex.Web/wwwroot/app",
    emptyOutDir: true,
    cssCodeSplit: false,
    // A bundle mounted into a server-rendered page, not an SPA: no index.html entry,
    // one self-contained script plus one stylesheet.
    lib: { entry: "src/main.tsx", name: "LexWorkspace", formats: ["iife"], fileName: () => "workspace.js" },
    rollupOptions: { output: { assetFileNames: "workspace.[ext]" } },
  },
  server: { proxy: { "/api": "http://localhost:5099", "/mcp": "http://localhost:5099" } },
});
