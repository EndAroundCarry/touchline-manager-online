# Changelog

All notable changes to Touchline Manager Online. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html) for releases. Match-engine and rule-set versions are tracked separately (plan §8.2, game-rules.md).

## [Unreleased]

### Added

- Stage 0: adopted master plan, 8 ADRs, game rules reference, C4 + ER diagrams, glossary, content/legal rules, threat model and data classification.
- Stage 1: monorepo scaffold — pinned .NET 10 / Angular 22 toolchain, 14 .NET projects, architecture tests, PostgreSQL-backed durable job queue with a no-op job end-to-end, Docker Compose (PostgreSQL 17 + Mailpit), GitHub Actions PR pipeline, `npm run dev` one-command start.
