# Documentation Map

This page records the canonical documentation locations for the Qobuzarr project.

## `wiki/` — Canonical Wiki (GitHub-wiki source)

The authoritative user-facing wiki. These pages are published to the GitHub Wiki and are the single source of truth for guides.

| Page | Topic |
|------|-------|
| [Home](../wiki/Home.md) | Project overview, quick links, requirements |
| [Installation Guide](../wiki/Installation-Guide.md) | Docker, native, Kubernetes, and build-from-source setup |
| [Configuration Guide](../wiki/Configuration-Guide.md) | Authentication, indexer/download-client settings, environment variables |
| [Troubleshooting](../wiki/Troubleshooting.md) | Diagnostics, log analysis, common fixes |
| [API Reference](../wiki/API-Reference.md) | Public APIs, services, and interfaces |
| [Plugin Development](../wiki/Plugin-Development.md) | Extending Qobuzarr, ML models, custom integrations |
| [Security Features](../wiki/Security-Features.md) | Defence-in-depth model, credential protection |

## `docs/` — Deep In-Repo Documentation

Technical and operational references organised by audience. See [docs/README.md](README.md) for the full index.

Highlights: architecture, CI/CD, testing guides, shared-library references, and advanced topics.

## Deprecated Trees

The following directories are **superseded** by `wiki/` and `docs/`. They are retained for reference only; do not update them.

| Directory | Status | Canonical replacement |
|-----------|--------|-----------------------|
| `wiki-content/` | Deprecated | `wiki/` |
| `docs/wiki/` | Deprecated | `wiki/` |
