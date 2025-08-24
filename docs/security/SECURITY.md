# Security Policy

## Supported Versions

Currently supported versions for security updates:

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |
| < 1.0   | :x:                |

## Reporting a Vulnerability

We take security vulnerabilities seriously. If you discover a security issue, please follow these steps:

### 1. Do NOT Create a Public Issue

Security vulnerabilities should **never** be reported via public GitHub issues.

### 2. Report Privately

Send details to: security@qobuzzarr.dev (or create a GitHub Security Advisory)

Include:
- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

### 3. Response Timeline

- **Initial Response**: Within 48 hours
- **Status Update**: Within 5 business days
- **Resolution Target**: 30 days for critical issues

## Security Best Practices

### For Users

1. **Credentials Storage**
   - Never share your Qobuz credentials
   - Use unique passwords
   - Enable 2FA on your Qobuz account if available

2. **Configuration Security**
   - Restrict access to Lidarr config files
   - Use proper file permissions
   - Don't expose Lidarr to the internet without authentication

3. **Network Security**
   - Use HTTPS for Lidarr web interface
   - Configure firewall rules appropriately
   - Use VPN if accessing remotely

### For Developers

1. **Code Security**
   ```csharp
   // NEVER log sensitive data
   _logger.Debug("Authenticating user: {0}", email); // OK
   _logger.Debug("Password: {0}", password); // NEVER DO THIS
   ```

2. **API Security**
   - Always use parameterized queries
   - Validate all input
   - Sanitize output
   - Use HTTPS for all API calls

3. **Dependency Security**
   - Keep dependencies updated
   - Review security advisories
   - Use `dotnet list package --vulnerable`

## Security Features

### Current Implementation

1. **Authentication**
   - MD5 password hashing (Qobuz requirement)
   - Secure session storage
   - Automatic session expiry

2. **API Communication**
   - HTTPS only
   - Request signing for sensitive endpoints
   - No credentials in URLs

3. **Data Protection**
   - No password storage (only hashes)
   - Secure credential handling
   - No sensitive data in logs

### Known Limitations

1. **MD5 Hashing**
   - Required by Qobuz API
   - Not ideal but unavoidable
   - Passwords still transmitted over HTTPS

2. **Session Storage**
   - Sessions stored in memory cache
   - Cleared on application restart
   - No persistent storage of tokens

## Security Checklist

### Before Release
- [ ] No hardcoded credentials
- [ ] No sensitive data in logs
- [ ] All user input validated
- [ ] Dependencies updated
- [ ] Security scan performed

### Regular Maintenance
- [ ] Monitor security advisories
- [ ] Update dependencies monthly
- [ ] Review authentication logs
- [ ] Check for unusual API usage

## Vulnerability Disclosure

After a security issue is resolved:

1. **Security Advisory** will be published
2. **CHANGELOG** will note the fix
3. **Users** will be notified to update

## Contact

- Security Email: security@qobuzzarr.dev
- PGP Key: [Available on request]
- GitHub Security Advisories: [Enable notifications]

## Acknowledgments

We appreciate responsible disclosure and may acknowledge security researchers who:
- Follow this policy
- Provide detailed reports
- Allow time for fixes
- Don't exploit vulnerabilities

Thank you for helping keep Qobuzzarr secure!