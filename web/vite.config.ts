import { reactRouter } from "@react-router/dev/vite";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [tailwindcss(), reactRouter()],
  resolve: {
    tsconfigPaths: true,
  },
  server: {
    // In dev the browser talks only to Vite; /api is forwarded to Kestrel
    // (api/Properties/launchSettings.json, profile "http").
    proxy: {
      "/api": "http://localhost:5143",
    },
  },
});
