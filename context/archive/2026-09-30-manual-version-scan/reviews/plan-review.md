<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Ręczny skan wersji i lista podatności (S-02)

- **Plan**: context/changes/manual-version-scan/plan.md
- **Mode**: Deep
- **Date**: 2026-10-01
- **Verdict**: REVISE → SOUND po poprawkach
- **Findings**: 1 critical, 4 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | FAIL |
| Plan Completeness | WARNING |

## Grounding
18/18 paths ✓, symbols ✓, brief↔plan ✓, Progress↔Phase 34/34 ✓ (po poprawkach 35/35)

## Findings

### F1 — Klon dziedziczy limit 30 s i abort przy wolnym transferze
- **Severity**: CRITICAL · **Impact**: LOW · **Dimension**: Blind Spots · **Location**: Faza 1, Faza 3
- **Detail**: `Git:TimeoutSeconds` (domyślnie 30) i `http.lowSpeedTime=20` przerwałyby realny klon.
- **Fix**: osobny `Scan:CloneTimeoutMinutes`, łagodniejszy lowSpeedTime dla clone, test argumentów.
- **Decision**: FIXED

### F2 — Worker i API zapisują ten sam wiersz wzorca bez ochrony
- **Severity**: WARNING · **Impact**: MEDIUM · **Dimension**: Architectural Fitness · **Location**: Faza 4
- **Fix A ⭐**: worker nie zapisuje `LastResolved*`. **Fix B**: token współbieżności xmin.
- **Decision**: FIXED (Fix A)

### F3 — Faza 6 pomija etykietę audytu i metadane skanu
- **Severity**: WARNING · **Impact**: LOW · **Dimension**: Plan Completeness · **Location**: Faza 6
- **Fix**: rozszerzyć `AuditAction`/`describeEvent`, wypisać metadane na stronie skanu.
- **Decision**: FIXED

### F4 — „Ostatni skan" bez definicji i bez planu zapytania
- **Severity**: WARNING · **Impact**: LOW · **Dimension**: Plan Completeness · **Location**: Faza 5
- **Fix**: najwyższe `Id` na `PatternId`, jedno zapytanie tylko w `GET /api/repos/{id}`.
- **Decision**: FIXED

### F5 — Usunięty lub nieaktywny wzorzec w kolejce nie ma przypadku
- **Severity**: WARNING · **Impact**: LOW · **Dimension**: Blind Spots · **Location**: Faza 4
- **Fix**: `Failed(PatternNotResolved)` z detalem, test.
- **Decision**: FIXED

### F6 — Skan w kolejce bez działającego workera czeka w nieskończoność
- **Severity**: OBSERVATION · **Impact**: LOW · **Dimension**: Blind Spots · **Location**: Faza 6
- **Fix**: ostrzeżenie po 2 minutach w `Queued`.
- **Decision**: FIXED

### F7 — Trivy może próbować pobrać bazę Java przy plikach .jar
- **Severity**: OBSERVATION · **Impact**: LOW · **Dimension**: Blind Spots · **Location**: Faza 3
- **Fix**: sprawdzić `--skip-java-db-update` / `--java-db-repository` przy implementacji.
- **Decision**: FIXED

### F8 — Referencja api.Tests do worker może zderzyć dwa Program
- **Severity**: OBSERVATION · **Impact**: LOW · **Dimension**: Plan Completeness · **Location**: Faza 4
- **Fix**: `api.Tests` nie referencjonuje `worker`.
- **Decision**: FIXED
