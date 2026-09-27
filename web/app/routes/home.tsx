import type { Route } from "./+types/home";

export function meta({}: Route.MetaArgs) {
  return [{ title: "SecurityCheck Portal" }];
}

export default function Home() {
  return (
    <main className="container mx-auto p-4">
      <h1 className="mb-6 text-2xl font-semibold">SecurityCheck Portal</h1>
      <p className="rounded-2xl border border-dashed border-gray-300 p-8 text-center text-gray-600 dark:border-gray-700 dark:text-gray-400">
        Nie dodano jeszcze żadnego repozytorium.
      </p>
    </main>
  );
}
