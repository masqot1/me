# Gate 0.16 — Deterministic Release Bundle

Creates a deterministic `.twcrelease` ZIP containing a V0.11 portable project and a small release descriptor. Verification checks outer SHA-256, embedded portable integrity, content root, release seal, sealed payload root, and readiness fingerprint. Import materializes only a bundle that passes the complete verification chain and verifies the embedded seal again after import.
