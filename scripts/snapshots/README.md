# Qobuzarr Screenshot Automation

Automated screenshot generation for Qobuzarr documentation.

## Prerequisites

- Node.js 20+
- Playwright: `npm i -D playwright && npx playwright install --with-deps chromium`
- Running Lidarr instance with Qobuzarr plugin installed

## Usage

### Local Development

```bash
# Install dependencies
npm i -D playwright
npx playwright install --with-deps chromium

# Run with default Lidarr URL (http://localhost:8686)
node scripts/snapshots/snap.mjs

# Run with custom Lidarr URL
LIDARR_BASE_URL=http://192.168.1.100:8686 node scripts/snapshots/snap.mjs
```

### CI/CD

Screenshots are automatically generated:
- On push to `main` (when plugin or snapshot files change)
- Weekly on Sunday at 7:00 UTC
- On manual workflow dispatch

Screenshots are committed back to the repository automatically.

## Generated Screenshots

| Screenshot | Description |
|------------|-------------|
| `landing.png` | Lidarr home page |
| `settings.png` | Settings overview |
| `indexers-list.png` | Indexers section |
| `indexer-add-modal.png` | Add Indexer modal with Qobuzarr search |
| `indexer-config.png` | Qobuzarr Indexer configuration |
| `download-clients-list.png` | Download Clients section |
| `download-client-add-modal.png` | Add Download Client modal |
| `download-client-config.png` | Qobuzarr Download Client configuration |

## Output

Screenshots are saved to `docs/assets/screenshots/` as PNG files.

## Troubleshooting

### "skip [name]: timeout" errors

The Lidarr instance may not have the plugin loaded. Verify:
1. Plugin files are in the correct directory
2. Lidarr has been restarted after plugin installation
3. Plugin appears in Settings > General > Plugins

### Screenshots are blank or wrong

Check that:
1. Lidarr is fully loaded (not showing setup wizard)
2. The plugin is properly discovered
3. No authentication is blocking access

## Query Intelligence Note

Qobuzarr includes ML-powered Query Intelligence which improves search over time. Screenshots may show different results as the model learns patterns.
