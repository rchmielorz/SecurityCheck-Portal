import {
  type RouteConfig,
  index,
  layout,
  route,
} from "@react-router/dev/routes";

export default [
  route("login", "routes/login.tsx"),
  layout("routes/app-layout.tsx", [
    index("routes/home.tsx"),
    route("repos/:repoId", "routes/repo-details.tsx"),
    // Catch-all under the protected layout, so unknown paths hit requireUser()
    // first and an anonymous deep link lands on /login?next=<path>.
    route("*", "routes/not-found.tsx"),
  ]),
] satisfies RouteConfig;
