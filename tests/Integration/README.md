# Qobuzarr Integration Tests

This directory contains integration tests that run against a real Lidarr instance with the Qobuzarr plugin installed.

## Setup

1. Copy `.env.example` to `.env` and configure:
   - `LIDARR_URL`: Your Lidarr instance URL (e.g., `http://localhost:8686`)
   - `LIDARR_API_KEY`: Your Lidarr API key
   - `DOCKER_CONTAINER_NAME`: Name of your Lidarr Docker container (optional, for log monitoring)

2. Configure Qobuz authentication (choose one method):
   - **Method 1**: Username/Password
     - `QOBUZ_USERNAME`: Your Qobuz username
     - `QOBUZ_PASSWORD`: Your Qobuz password
   - **Method 2**: User ID/Token
     - `QOBUZ_USER_ID`: Your Qobuz user ID
     - `QOBUZ_USER_AUTH_TOKEN`: Your Qobuz auth token

3. App credentials are automatically loaded from the plugin codebase defaults, but you can override:
   - `QOBUZ_APP_ID`: Custom Qobuz app ID (optional)
   - `QOBUZ_APP_SECRET`: Custom Qobuz app secret (optional)

4. Ensure your Lidarr instance has:
   - Qobuzarr plugin installed and configured
   - Qobuzarr set as the only enabled indexer and download client
   - At least some artists added (for random selection)

3. Run the tests:
   ```bash
   dotnet test tests/Integration/
   ```

## Test Types

- **SmokeTests**: Basic connectivity and configuration validation
- **RandomDownloadTests**: Automated random album download testing
- **LogAnalysisTests**: Docker log monitoring and analysis
- **LearningTests**: Adaptive testing that learns from previous runs