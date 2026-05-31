# Live Integration Testing Guide

This guide helps you test the Qobuzarr plugin against your actual Lidarr instance with automated deployment, log monitoring, and comprehensive validation.

## Quick Start

### 1. Configure Your Environment

Copy the example configuration:

```bash
cp tests/Integration/.env.example tests/Integration/.env
```

Edit `tests/Integration/.env` with your settings:

```bash
# Your Lidarr instance
LIDARR_URL=http://192.168.1.100:8686
LIDARR_API_KEY=your_actual_api_key_here

# Your Docker container (for automation)
DOCKER_CONTAINER_NAME=lidarr

# Your Qobuz credentials
QOBUZ_USERNAME=your.email@example.com
QOBUZ_PASSWORD=your_password
```

### 2. Run Tests

**Quick Test** (recommended first):

```powershell
.\run-live-tests.ps1 -TestFilter "Critical"
```

**Full Test with Deployment**:

```powershell
.\run-live-tests.ps1 -DockerContainer "your-container-name"
```

**Security-Focused Test**:

```powershell
.\run-live-tests.ps1 -TestFilter "Security" -Verbose
```

## Test Categories

### Critical Tests (Must Pass)

- ✅ Plugin loading and configuration validation
- ✅ Basic search functionality with known albums
- ✅ Plugin restart resilience
- ✅ Security input validation (NEW)

### High Priority Tests

- ✅ Plugin error handling and resilience
- ✅ Download queue integration
- ✅ Authentication security in live environment

### Medium/Low Priority Tests

- ✅ Performance and resource usage
- ✅ End-to-end workflow validation
- ✅ Security documentation validation

## Docker/Unraid Integration

### Docker Features

- **Automated deployment**: Copies plugin files directly to container
- **Log monitoring**: Real-time log analysis during tests
- **Container restart**: Validates plugin resilience
- **Health checking**: Waits for Lidarr to come back online

### Unraid Features

- **API integration**: Connects to Unraid management API
- **Container management**: Start/stop/restart containers
- **File deployment**: Secure file transfer to Unraid systems

## Test Scenarios

### 1. Plugin Loading Validation

```
✅ Validates Qobuzarr indexer is loaded and enabled
✅ Validates Qobuzarr download client (if configured)
✅ Checks plugin configuration accessibility
```

### 2. Search Functionality Testing

```
✅ Gets a wanted album from your Lidarr
✅ Triggers search via Lidarr API
✅ Monitors search progress and completion
✅ Validates releases are found (when available)
```

### 3. Security Testing (NEW)

```
✅ Tests InputSanitizer email validation
✅ Tests search query sanitization (SQL/XSS prevention)
✅ Tests path traversal prevention
✅ Tests credential validation
✅ Monitors for credential leaks in logs
```

### 4. Download Integration Testing

```
✅ Tests download queue integration
✅ Validates download command processing
✅ Monitors download progress
```

### 5. Restart Resilience Testing

```
✅ Restarts Lidarr (Docker automation)
✅ Waits for service to come back online
✅ Validates plugin is still loaded and functional
```

## Environment Configuration Options

### Basic Configuration

```bash
# Required
LIDARR_URL=http://your-lidarr:8686
LIDARR_API_KEY=your_api_key

# Docker (optional but recommended)
DOCKER_CONTAINER_NAME=lidarr
```

### Advanced Configuration

```bash
# Unraid integration
UNRAID_HOST=http://your-unraid:8080
UNRAID_API_KEY=your_unraid_api_key

# Testing options
TEST_TIMEOUT_MINUTES=5
VERBOSE_LOGGING=true
SKIP_FILESYSTEM_TESTS=false

# Qobuz authentication (for end-to-end testing)
QOBUZ_USERNAME=your.email@example.com
QOBUZ_PASSWORD=your_password
```

## Usage Examples

### Developer Workflow

```powershell
# 1. Quick test after code changes
.\run-live-tests.ps1 -TestFilter "Critical"

# 2. Full test before committing
.\run-live-tests.ps1 -BuildFirst -DeployPlugin

# 3. Security-focused testing
.\run-live-tests.ps1 -TestFilter "Security" -Verbose
```

### CI/CD Integration

```bash
# In your CI pipeline
export LIDARR_URL="http://test-lidarr:8686"
export LIDARR_API_KEY="$TEST_API_KEY"
export DOCKER_CONTAINER_NAME="lidarr-test"

./run-live-tests.sh --no-build --filter Critical
```

### Production Validation

```powershell
# Test against production Lidarr (read-only)
$env:LIDARR_URL = "http://production-lidarr:8686"
$env:LIDARR_API_KEY = "prod_api_key"

.\run-live-tests.ps1 -SkipBuild -TestFilter "Critical"
```

## Interpreting Test Results

### ✅ Success Indicators

- All critical tests pass
- No security exceptions in logs
- Plugin loads and configures correctly
- Search operations complete successfully

### ⚠️ Warning Indicators  

- Some non-critical tests fail (acceptable)
- Performance slower than expected
- Log warnings (may be normal)
- Download tests incomplete (if no suitable releases)

### ❌ Failure Indicators

- Plugin fails to load
- Security exceptions detected
- Critical functionality broken
- Authentication failures

## Troubleshooting

### Common Issues

**"Cannot connect to Lidarr"**

- Check LIDARR_URL is correct and accessible
- Verify LIDARR_API_KEY is valid
- Ensure Lidarr is running and responsive

**"Plugin not found"**

- Ensure plugin is built and deployed
- Check Lidarr plugins directory
- Restart Lidarr after deployment

**"Docker commands fail"**

- Verify Docker is installed and running
- Check container name is correct
- Ensure Docker daemon is accessible

**"Tests time out"**

- Increase TEST_TIMEOUT_MINUTES
- Check Lidarr performance and load
- Verify network connectivity

### Log Analysis

The tests automatically monitor logs and categorize issues:

- **Errors**: Critical issues that need immediate attention
- **Warnings**: Potential issues worth investigating
- **Info**: Normal operation confirmations

### Security Validation

New security tests validate:

- ✅ Input sanitization prevents injection attacks
- ✅ No credential leaks in logs
- ✅ Path traversal prevention works
- ✅ Malicious input detection functions

## Advanced Features

### Custom Test Scenarios

You can extend the testing framework by:

1. Adding new test methods to `ComprehensiveLiveTests.cs`
2. Creating specialized test classes for specific scenarios
3. Implementing custom validation logic in `LiveLidarrIntegrationFramework.cs`

### Continuous Integration

The testing framework is designed for CI/CD integration:

- Environment variable configuration
- Docker automation support
- Exit codes for build pipeline integration
- Structured test result reporting

### Monitoring Integration

Connect with monitoring systems:

- Export test results to metrics systems
- Integrate with alerting for test failures
- Track performance trends over time

## Security Testing Focus

Given the new security improvements, pay special attention to:

1. **Input Validation Tests**: Ensure all user inputs are properly sanitized
2. **Authentication Security**: Verify no credentials leak in logs
3. **API Security**: Confirm malicious inputs are blocked
4. **File System Security**: Validate path traversal prevention

## Next Steps

After running live tests successfully:

1. Monitor production Lidarr logs for any issues
2. Test manual operations in Lidarr UI
3. Validate search and download functionality
4. Consider adding custom test scenarios for your specific use cases

The live testing system provides confidence that your Qobuzarr plugin works correctly in real-world scenarios with proper security protections.
