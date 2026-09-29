import { Link } from "react-router";

import type { Route } from "./+types/not-found";
import { Card } from "../components/card";
import { linkClass } from "../components/styles";

export function meta({}: Route.MetaArgs) {
  return [{ title: "Nie znaleziono strony – SecurityCheck Portal" }];
}

// Catch-all child of the protected layout: anonymous users are redirected to
// /login by the layout's clientLoader before this renders.
export default function NotFound() {
  return (
    <main className="container mx-auto flex justify-center p-4">
      <Card className="mt-8 w-full max-w-md space-y-3">
        <h1 className="text-2xl font-semibold">Nie znaleziono strony</h1>
        <p className="text-text-muted">Adres, który otworzyłeś, nie istnieje lub został przeniesiony.</p>
        <Link to="/" className={linkClass}>
          Wróć na stronę główną
        </Link>
      </Card>
    </main>
  );
}
