import { useEffect, useState, type ReactNode } from "react";

import { Alert, type AlertVariant } from "../components/alert";
import { Badge, BADGE_KINDS } from "../components/badge";
import { Button } from "../components/button";
import { Card } from "../components/card";
import { ConfirmDialog } from "../components/confirm-dialog";
import { Field, Input } from "../components/field";
import { focusRing } from "../components/styles";
import { ThemeToggle } from "../components/theme-toggle";

// No `+types` import: typegen runs with NODE_ENV=production, so this dev-only
// route gets no generated types there.
export function meta() {
  return [{ title: "Styleguide – SecurityCheck Portal" }];
}

const GROUPS: Array<{ title: string; tokens: string[] }> = [
  {
    title: "Marka",
    tokens: ["primary", "primary-hover", "primary-fg", "primary-soft", "primary-text", "ring", "secondary", "secondary-fg"],
  },
  {
    title: "Powierzchnie i tekst",
    tokens: [
      "surface",
      "surface-muted",
      "surface-raised",
      "surface-hover",
      "border",
      "border-subtle",
      "text",
      "text-muted",
      "text-subtle",
    ],
  },
  ...["critical", "high", "medium", "low", "success", "warning", "danger", "info", "neutral", "new", "accepted-risk"].map(
    (name) => ({ title: name, tokens: [name, `${name}-soft`, `${name}-fg`] }),
  ),
];

const FAMILIES = ["critical", "high", "medium", "low", "success", "warning", "danger", "info", "neutral", "new", "accepted-risk"];

// [foreground token, background token]
const PAIRS: Array<[string, string]> = [
  ["text", "surface"],
  ["text-muted", "surface"],
  ["text-subtle", "surface"],
  ["primary-fg", "primary"],
  ["primary-text", "surface"],
  ["text-muted", "surface-muted"],
  ["secondary-fg", "secondary"],
  ...FAMILIES.map((name): [string, string] => [`${name}-fg`, `${name}-soft`]),
];

const AA = 4.5;

function parseHex(value: string): [number, number, number] | null {
  const match = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(value.trim());
  if (!match) return null;
  let hex = match[1];
  if (hex.length === 3) hex = [...hex].map((c) => c + c).join("");
  const n = parseInt(hex, 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

function luminance([r, g, b]: [number, number, number]): number {
  const channel = (v: number) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

function contrast(a: string, b: string): number | null {
  const ca = parseHex(a);
  const cb = parseHex(b);
  if (!ca || !cb) return null;
  const [hi, lo] = [luminance(ca), luminance(cb)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const ALL_TOKENS = [...new Set(GROUPS.flatMap((g) => g.tokens))];

function readTokens(): Record<string, string> {
  const style = getComputedStyle(document.documentElement);
  return Object.fromEntries(ALL_TOKENS.map((t) => [t, style.getPropertyValue(`--color-${t}`).trim()]));
}

// Current token values; re-read whenever the `dark` class on <html> changes.
function useTokens(): { values: Record<string, string>; dark: boolean } {
  const [state, setState] = useState<{ values: Record<string, string>; dark: boolean }>({
    values: {},
    dark: false,
  });
  useEffect(() => {
    const update = () =>
      setState({ values: readTokens(), dark: document.documentElement.classList.contains("dark") });
    update();
    const observer = new MutationObserver(update);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
    return () => observer.disconnect();
  }, []);
  return state;
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="space-y-4">
      <h2 className="border-b border-border-subtle pb-2 text-xl font-semibold">{title}</h2>
      {children}
    </section>
  );
}

function Swatch({ token, value }: { token: string; value: string }) {
  return (
    <div className="flex items-center gap-3">
      <span
        className="h-8 w-8 shrink-0 rounded-lg border border-border"
        style={{ backgroundColor: `var(--color-${token})` }}
        aria-hidden="true"
      />
      <span className="min-w-0 text-sm">
        <span className="block font-mono">{token}</span>
        <span className="block font-mono text-xs text-text-muted">{value || "—"}</span>
      </span>
    </div>
  );
}

function Palette({ values }: { values: Record<string, string> }) {
  return (
    <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
      {GROUPS.map((group) => (
        <Card key={group.title} className="space-y-3 p-4">
          <h3 className="text-sm font-semibold uppercase tracking-wide text-text-muted">{group.title}</h3>
          {group.tokens.map((token) => (
            <Swatch key={token} token={token} value={values[token] ?? ""} />
          ))}
        </Card>
      ))}
    </div>
  );
}

function Contrasts({ values }: { values: Record<string, string> }) {
  return (
    <div className="overflow-x-auto rounded-2xl border border-border-subtle">
      <table className="w-full text-left text-sm">
        <thead className="bg-surface-muted text-text-muted">
          <tr>
            <th scope="col" className="px-3 py-2 font-medium">Para (tekst na tle)</th>
            <th scope="col" className="px-3 py-2 font-medium">Podgląd</th>
            <th scope="col" className="px-3 py-2 font-medium">Kontrast</th>
            <th scope="col" className="px-3 py-2 font-medium">AA 4,5:1</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border-subtle">
          {PAIRS.map(([fg, bg]) => {
            const ratio = values[fg] && values[bg] ? contrast(values[fg], values[bg]) : null;
            const pass = ratio !== null && ratio >= AA;
            return (
              <tr key={`${fg}/${bg}`}>
                <td className="px-3 py-2 font-mono">
                  {fg} / {bg}
                </td>
                <td className="px-3 py-2">
                  <span
                    className="rounded px-2 py-0.5"
                    style={{ color: `var(--color-${fg})`, backgroundColor: `var(--color-${bg})` }}
                  >
                    Aa
                  </span>
                </td>
                <td className="px-3 py-2 font-mono">{ratio === null ? "—" : `${ratio.toFixed(2)}:1`}</td>
                <td className="px-3 py-2">
                  {ratio === null ? (
                    "—"
                  ) : pass ? (
                    <Badge kind="success">PASS</Badge>
                  ) : (
                    <Badge kind="danger">FAIL</Badge>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

const ALERTS: Array<[AlertVariant, string]> = [
  ["error", "Nieprawidłowy login lub hasło."],
  ["warning", "Repozytorium ma nieaktywne wzorce."],
  ["info", "Sprawdzanie może potrwać kilka sekund."],
  ["success", "Wzorzec został dodany."],
];

export default function Styleguide() {
  const { values, dark } = useTokens();
  const [dialogOpen, setDialogOpen] = useState(false);
  const [dialogResult, setDialogResult] = useState("Brak akcji");

  return (
    <main className="container mx-auto space-y-10 p-4">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">Styleguide</h1>
          <p className="text-sm text-text-muted">
            Aktualny motyw: {dark ? "ciemny" : "jasny"}. Strona dostępna tylko w trybie deweloperskim; nie pokazuje danych
            aplikacji.
          </p>
        </div>
        <ThemeToggle />
      </header>

      <Section title="Kolory">
        <Palette values={values} />
        <h3 className="text-lg font-semibold">Kontrast (WCAG AA)</h3>
        <Contrasts values={values} />
      </Section>

      <Section title="Typografia">
        <div className="space-y-2">
          <p className="text-2xl font-semibold">Nagłówek h1 – SecurityCheck Portal</p>
          <p className="text-lg font-semibold">Nagłówek h2 – Wzorce wersji</p>
          <p>Tekst podstawowy – Sprawdzanie wersji w repozytorium.</p>
          <p className="text-sm text-text-muted">Tekst mały, przygaszony – Dodano wczoraj przez admina.</p>
          <p className="text-xs text-text-subtle">Tekst pomocniczy – 2025-01-01 12:00</p>
          <p className="font-mono">Mono – 2.1.* / a1b2c3d</p>
          <p>
            <a href="#typografia" className={`text-primary-text underline ${focusRing}`}>
              Przykładowy link
            </a>
          </p>
        </div>
      </Section>

      <Section title="Komponenty">
        <div className="space-y-3">
          <h3 className="text-lg font-semibold">Button</h3>
          <div className="flex flex-wrap gap-3">
            <Button>Zapisz</Button>
            <Button variant="secondary">Sprawdź</Button>
            <Button variant="danger">Usuń</Button>
            <Button disabled>Zapisz (wyłączony)</Button>
            <Button variant="secondary" disabled>
              Sprawdź (wyłączony)
            </Button>
            <Button variant="danger" disabled>
              Usuń (wyłączony)
            </Button>
          </div>
        </div>

        <div className="space-y-3">
          <h3 className="text-lg font-semibold">Field i Input</h3>
          <div className="grid max-w-3xl gap-4 sm:grid-cols-2">
            <Field label="Login">{(control) => <Input name="login" placeholder="np. jan.kowalski" {...control} />}</Field>
            <Field label="Wzorzec" error="Wzorzec musi mieć postać X.Y.*, np. 2.1.*.">
              {(control) => <Input name="pattern" mono defaultValue="2.x" {...control} />}
            </Field>
            <Field label="Pole wyłączone">{(control) => <Input disabled defaultValue="Wyłączone" {...control} />}</Field>
          </div>
        </div>

        <div className="space-y-3">
          <h3 className="text-lg font-semibold">Alert</h3>
          <div className="max-w-2xl space-y-2">
            {ALERTS.map(([variant, text]) => (
              <Alert key={variant} variant={variant}>
                {text}
              </Alert>
            ))}
          </div>
        </div>

        <div className="space-y-3">
          <h3 className="text-lg font-semibold">Card</h3>
          <Card className="max-w-md">
            <h4 className="mb-2 text-lg font-semibold">Dodaj repozytorium</h4>
            <p className="text-sm text-text-muted">Karta z ramką, cieniem i tłem podniesionej powierzchni.</p>
          </Card>
        </div>

        <div className="space-y-3">
          <h3 className="text-lg font-semibold">Badge</h3>
          <div className="flex flex-wrap gap-2">
            {BADGE_KINDS.map((kind) => (
              <Badge key={kind} kind={kind} />
            ))}
          </div>
        </div>

        <div className="space-y-3">
          <h3 className="text-lg font-semibold">ConfirmDialog</h3>
          <div className="flex flex-wrap items-center gap-3">
            <Button variant="danger" onClick={() => setDialogOpen(true)}>
              Usuń wzorzec 2.1.*
            </Button>
            <span className="text-sm text-text-muted" aria-live="polite">
              Wynik: {dialogResult}
            </span>
          </div>
          <ConfirmDialog
            open={dialogOpen}
            title="Usunąć wzorzec 2.1.*?"
            description="Tej operacji nie można cofnąć."
            confirmLabel="Usuń"
            onConfirm={() => {
              setDialogOpen(false);
              setDialogResult("Potwierdzono");
            }}
            onCancel={() => {
              setDialogOpen(false);
              setDialogResult("Anulowano");
            }}
          />
        </div>
      </Section>
    </main>
  );
}
