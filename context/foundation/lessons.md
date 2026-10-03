# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Recovery po restarcie musi zakładać jednego właściciela

- **Context**: core/Scanning/ScanQueue.cs:69, WorkDirectory.DeleteOrphans (manual-version-scan, F2)
- **Problem**: RecoverInterruptedAsync oznacza Interrupted każdy skan Running i kasuje każdy numeryczny folder w WorkRoot, bez rozpoznania właściciela. Drugi worker albo nakładający się restart zabije żywy skan i jego checkout.
- **Rule**: Kod odzyskiwania po restarcie (zmiana stanu i sprzątanie plików) może działać tylko, gdy proces udowodni wyłączność (np. advisory lock w bazie albo identyfikator instancji zapisany przy claimie); założenie "jeden worker" musi być wymuszone, nie tylko opisane.
- **Applies to**: każdy background worker i kolejka oparta na tabeli (core/Scanning, worker/)
