# Gate 0.15 — Verified Release Seal

Creates immutable `_release/release-seal.json` only for a READY project. The seal binds the readiness fingerprint, payload content-root SHA-256, immutable snapshot IDs, completeness/visual metrics, and hashes of release evidence. `_release`, `_twc_package`, and `_twc_catalog` are excluded from the payload root so verified transport metadata does not invalidate the sealed payload.
