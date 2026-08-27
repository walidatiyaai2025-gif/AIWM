# AIWM WordPress Web Edition

Status: **IN PROGRESS — NOT DEMO-READY**

This directory is the WordPress-hosted Web Edition variant of AI WordPress Manager.

Canonical acceptance authority: Issue #11.

## Product contract

The Web Edition preserves the current AIWM primary journey and visual language while replacing the WPF/.NET runtime with a WordPress-native web runtime suitable for standard WordPress hosting.

Primary navigation:

1. Dashboard
2. Sites
3. WordPress Explorer
4. SEO Audit
5. Suggested Changes
6. Approval Queue
7. Execution Center
8. Evidence Center
9. Settings / AI Providers

## Runtime architecture

- PHP 8.x WordPress plugin host
- React + TypeScript admin SPA
- WordPress REST API application boundary
- MySQL/MariaDB custom persistence tables
- Batched/background execution for audits, AI calls and mutations
- Server-side AI provider credentials
- Capability + nonce + REST permission enforcement

## Non-negotiable gates

- No fake/demo data presented as real.
- No dead controls or placeholder success states.
- No long blocking PHP requests for large jobs.
- No API secret exposed to browser bundles or REST responses.
- No completion claim before an installable plugin ZIP passes the complete functional demo journey.
- Visual parity is judged against the current AIWM desktop product, not against default WordPress admin styling.

## Initial layout

- `plugin/` — WordPress PHP shell and REST/persistence/runtime services.
- `ui/` — React/TypeScript application source.
- `docs/` — architecture, parity matrix, performance budget and demo evidence.

The next implementation slices must remain focused on Issue #11 until `DEMO-READY` is proven.