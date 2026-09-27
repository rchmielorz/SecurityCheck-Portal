import type { Config } from "@react-router/dev/config";

export default {
  // SPA mode: the build emits a static build/client/index.html that the API
  // serves from wwwroot (see scripts/copy-to-api.mjs).
  ssr: false,
} satisfies Config;
