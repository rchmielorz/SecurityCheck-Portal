import type { Route } from "./+types/not-found";

export function meta({}: Route.MetaArgs) {
  return [{ title: "Nie znaleziono strony – SecurityCheck Portal" }];
}

// Catch-all child of the protected layout: anonymous users are redirected to
// /login by the layout's clientLoader before this renders.
export default function NotFound() {
  return (
    <main className="container mx-auto p-4">
      <h1 className="text-2xl font-semibold">Nie znaleziono strony</h1>
    </main>
  );
}
