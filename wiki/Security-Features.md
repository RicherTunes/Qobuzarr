> **⚠️ Aspirational content**: Some APIs and features below (e.g. `SecureCredentialManager`, `SecurityMonitoringService`, some method signatures) are aspirational design docs — actual implementations may differ. Refer to `src/Security/` for the ground truth.

# Security Features

Comprehensive overview of Qobuzarr's security architecture, including credential protection, input validation, ML model security, and threat mitigation.

## 🛡️ Security Architecture Overview

Qobuzarr implements **defense-in-depth security** with multiple layers of protection:

### Security Design Principles

1. **Defense-in-Depth**: Multiple security layers at every component
2. **Least Privilege**: Minimal permissions and restricted access
3. **Secure by Default**: HTTPS-only, automatic credential masking
4. **Zero Trust**: All inputs validated regardless of source
5. **Fail Safe**: Secure defaults with explicit security policies

### Security Components

```
┌─────────────────────────────────────────┐
│           Application Layer             │
├─────────────────────────────────────────┤
│        Input Validation Layer           │
├─────────────────────────────────────────┤
│       Credential Security Layer         │
├─────────────────────────────────────────┤
│         ML Model Security               │
├─────────────────────────────────────────┤
│         API Security Layer              │
├─────────────────────────────────────────┤
│        Memory Protection Layer          │
└─────────────────────────────────────────┘
```

## 🔐 Credential Security

### SecureCredentialManager

**Enterprise-grade credential protection** with memory security.

```csharp
// Automatic secure credential handling
var credentialManager = new SecureCredentialManager(logger);

// Store with memory protection
credentialManager.StoreSecureCredential(\"qobuz_password\", userPassword);

// Use with automatic cleanup
await credentialManager.UseSecureCredentialAsync(\"qobuz_password\", async password =>
{
    return await qobuzApi.AuthenticateAsync(email, password);
    // Password automatically cleared from memory here
});
```

**Security Features:**

#### Memory Protection

- **SecureString Integration**: Windows SecureString API for protected memory
- **Automatic Cleanup**: Zero memory footprint after credential use
- **GC Prevention**: Prevents garbage collection of sensitive strings
- **Memory Encryption**: OS-level protection for credential storage

#### Access Control

- **Thread-Safe Operations**: Concurrent credential access patterns
- **Time-Limited Exposure**: Credentials exposed only during active use
- **Audit Logging**: All credential operations logged securely
- **Validation Policies**: Enforces credential format and strength requirements

#### Example: Secure Authentication Flow

```csharp
public async Task<QobuzSession> AuthenticateSecurelyAsync(string email, string password)
{
    using var credentialManager = new SecureCredentialManager(_logger);
    
    try 
    {
        // Store password securely
        credentialManager.StoreSecureCredential(\"auth_password\", password);
        
        // Use credential with automatic protection
        var session = await credentialManager.UseSecureCredentialAsync(\"auth_password\",
            async securePassword => 
            {
                _logger.LogDebug(\"Authenticating with masked credentials: {Email} / {Password}\",
                    email, credentialManager.MaskSensitiveData(securePassword));
                
                return await _qobuzApi.AuthenticateAsync(email, securePassword);
            });
            
        _logger.LogInfo(\"Authentication successful for user: {UserId}\", session.UserId);
        return session;
    }
    finally
    {
        // Credentials automatically cleared from memory
        _logger.LogDebug(\"Secure credential cleanup completed\");
    }
}
```

### Credential Masking and Logging

**Automatic sensitive data protection** in logs and debugging.

```csharp
var credentialManager = new SecureCredentialManager(logger);

// Automatic masking of sensitive data
string maskedPassword = credentialManager.MaskSensitiveData(\"mySecretPassword123\");
// Result: \"myS***rd123\" (preserves first/last chars for debugging)

// Secure logging patterns
_logger.LogDebug(\"Authentication attempt: {Email} / {Password}\", 
    email, credentialManager.MaskSensitiveData(password));
```

## 🤖 ML Model Security

### SecureMLModelLoader

**Comprehensive validation** for ML assemblies and models.

```csharp
var modelLoader = new SecureMLModelLoader(logger);

// Production: Require signature verification
var mlEngine = await modelLoader.LoadSecureModelAsync(\"/models/query-optimizer.dll\", 
    requireSignature: true);

// Development: Optional signature with warnings  
var devEngine = await modelLoader.LoadSecureModelAsync(\"/models/dev-model.dll\",
    requireSignature: false);
```

**Security Validation Pipeline:**

#### 1. Path Traversal Protection

```csharp
// Prevents malicious path access
var sanitizedPath = ValidateAndSanitizePath(modelPath);
// Blocks: \"../../../etc/passwd\", \"C:\\Windows\\System32\", etc.
```

#### 2. File Integrity Verification

```csharp
// Hash-based integrity checking
var expectedHash = GetExpectedModelHash(modelPath);
var actualHash = ComputeFileHash(modelPath);

if (!SecureHashEquals(expectedHash, actualHash))
{
    throw new SecurityException(\"ML model integrity check failed\");
}
```

#### 3. Assembly Signature Validation

```csharp
// Strong name and certificate validation
if (requireSignature && !IsAssemblySigned(assemblyPath))
{
    throw new SecurityException(\"ML assembly signature validation failed\");
}
```

#### 4. Secure Assembly Loading

```csharp
// Isolated assembly loading with restricted permissions
var loadContext = new SecureAssemblyLoadContext(assemblyPath);
var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

// Validate loaded types and interfaces
ValidateMLInterface(assembly);
```

### ML Model Security Configuration

```json
{
  \"mlSecurity\": {
    \"requireSignedModels\": true,
    \"allowedModelPaths\": [
      \"/config/plugins/models/\",
      \"/app/models/\"
    ],
    \"trustedPublishers\": [
      \"CN=RicherTunes, O=Qobuzarr Development\"
    ],
    \"maxModelSize\": \"50MB\",
    \"modelValidationTimeout\": \"30s\"
  }
}
```

## 🔒 Input Validation & Sanitization

### InputSanitizer

Compatibility facade for Qobuzarr input validation.

```csharp
var safeQuery = InputSanitizer.SanitizeSearchQuery(query);
var safeFileName = InputSanitizer.SanitizeFileName(fileName);
```

Shared helpers delegate through Common `Sanitize` where the Common contract matches the Qobuzarr API surface. Qobuz-specific authentication, file path, and metadata validators remain local in the facade.

### MetadataSanitizer

**Secure processing** of music metadata from external sources.

```csharp
public class MetadataSanitizer
{
    public QobuzTrack SanitizeTrackMetadata(QobuzTrack track)
    {
        return new QobuzTrack
        {
            Title = SanitizeTextContent(track.Title, maxLength: 200),
            Artist = SanitizeTextContent(track.Artist, maxLength: 100),
            Album = SanitizeTextContent(track.Album, maxLength: 200),
            Genre = SanitizeGenre(track.Genre),
            Year = ValidateYear(track.Year),
            Duration = ValidateDuration(track.Duration),
            // Remove potentially malicious URLs
            CoverUrl = SanitizeUrl(track.CoverUrl)
        };
    }
    
    private string SanitizeTextContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        
        // Remove control characters
        content = RemoveControlCharacters(content);
        
        // Remove HTML/script content
        content = RemoveHtmlContent(content);
        
        // Normalize whitespace
        content = NormalizeWhitespace(content);
        
        // Apply length limits
        return content.Length > maxLength ? content[..maxLength] : content;
    }
}
```

## 🌐 API Security

### SecureApiExtensions

**Enhanced API security** for all Qobuz API communications.

```csharp
public static class SecureApiExtensions
{
    public static async Task<TResponse> ExecuteSecureApiCallAsync<TResponse>(
        this IQobuzApiClient client,
        string endpoint,
        object parameters = null)
    {
        // 1. Input validation
        ValidateEndpoint(endpoint);
        ValidateParameters(parameters);

        // 2. Request signing
        var signedRequest = SignRequest(endpoint, parameters);

        // 3. Secure HTTP execution
        var response = await client.ExecuteAsync(signedRequest);

        // 4. Response validation
        ValidateResponse(response);

        // 5. Safe deserialization
        return DeserializeSecurely<TResponse>(response.Content);
    }
}
```

### Rate Limiting Security

**Adaptive rate limiting** uses a plugin adapter over Common's named limiter.

```csharp
public class AdaptiveRateLimiter : NamedServiceRateLimiter
{
    public AdaptiveRateLimiter() : base("Qobuz") { }
}
```

The adaptive algorithm, stats, and response recording are implemented by Common `NamedServiceRateLimiter`; Qobuzarr only supplies the local concrete type required for Lidarr auto-registration.

## 🔍 Security Monitoring

### SecurityConfigValidator

**Runtime security validation** and monitoring.

```csharp
public class SecurityConfigValidator
{
    public async Task<SecurityValidationResult> ValidateConfigurationAsync()
    {
        var results = new List<ValidationResult>();

        // Check credential security
        results.Add(await ValidateCredentialSecurityAsync());

        // Check ML model signatures
        results.Add(await ValidateMLModelSecurityAsync());

        // Check network security
        results.Add(await ValidateNetworkSecurityAsync());

        // Check logging security
        results.Add(await ValidateLoggingSecurityAsync());

        return new SecurityValidationResult(results);
    }
    
    private async Task<ValidationResult> ValidateCredentialSecurityAsync()
    {
        // Verify secure credential storage
        if (!IsSecureCredentialStorageEnabled())
        {
            return ValidationResult.Warning(\"Secure credential storage is disabled\");
        }
        
        // Check credential policies
        if (!AreCredentialPoliciesEnforced())
        {
            return ValidationResult.Error(\"Credential policies not enforced\");
        }
        
        return ValidationResult.Success(\"Credential security validated\");
    }
}
```

### Security Event Monitoring

**Real-time threat detection** and response.

```csharp
public class SecurityMonitoringService
{
    public async Task MonitorSecurityEventAsync(SecurityEvent securityEvent)
    {
        // Analyze event for threats
        var analysis = await AnalyzeSecurityEventAsync(securityEvent);
        
        // Record metrics
        _metrics.Increment($\"security.events.{securityEvent.Type}\");
        _metrics.RecordValue($\"security.threat_level\", (int)analysis.ThreatLevel);
        
        // Handle high-threat events
        if (analysis.ThreatLevel >= ThreatLevel.High)
        {
            await HandleHighThreatEventAsync(securityEvent, analysis);
        }
        
        // Update security models
        await UpdateSecurityModelsAsync(securityEvent, analysis);
    }
    
    private async Task HandleHighThreatEventAsync(SecurityEvent securityEvent, SecurityAnalysis analysis)
    {
        // Send alert
        await _alertService.SendSecurityAlertAsync(new SecurityAlert
        {
            Title = $\"High threat detected: {securityEvent.Type}\",
            Description = analysis.Description,
            Severity = Severity.Critical,
            SourceIp = securityEvent.SourceIp,
            Timestamp = DateTimeOffset.UtcNow,
            RecommendedActions = analysis.RecommendedActions
        });
        
        // Apply automatic mitigations
        if (analysis.RequiresImmediateAction)
        {
            await ApplySecurityMitigationsAsync(securityEvent.SourceIp);
        }
    }
}
```

## 🔐 Environment Security

### Secure Configuration

**Environment-based security settings** for production deployments.

```bash
# Credential Security
export QOBUZ_REQUIRE_SECURE_CREDENTIALS=true
export QOBUZ_CREDENTIAL_ENCRYPTION_KEY=base64-key-here

# ML Model Security
export QOBUZ_REQUIRE_SIGNED_MODELS=true
export QOBUZ_TRUSTED_MODEL_PATHS=\"/app/models:/config/models\"

# API Security
export QOBUZ_ENFORCE_HTTPS=true
export QOBUZ_API_REQUEST_SIGNING=true

# Monitoring Security
export QOBUZ_SECURITY_MONITORING=true
export QOBUZ_THREAT_DETECTION_LEVEL=medium

# Logging Security
export QOBUZ_MASK_SENSITIVE_DATA=true
export QOBUZ_SECURE_LOGGING=true
```

### Docker Security

**Container security** for Docker deployments.

```dockerfile
# Use non-root user
USER qobuzarr:qobuzarr

# Secure file permissions
COPY --chmod=600 models/ /app/models/
COPY --chmod=644 config/ /app/config/

# Security labels
LABEL security.non-root=true
LABEL security.no-new-privileges=true

# Health and security checks
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s \\
  CMD curl -f http://localhost:8080/health/security || exit 1
```

### Kubernetes Security

**Kubernetes security policies** and configurations.

```yaml
apiVersion: v1
kind: SecurityContext
metadata:
  name: qobuzarr-security-context
spec:
  runAsNonRoot: true
  runAsUser: 1000
  runAsGroup: 1000
  allowPrivilegeEscalation: false
  capabilities:
    drop:
      - ALL
  seccompProfile:
    type: RuntimeDefault
  readOnlyRootFilesystem: true

---
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy  
metadata:
  name: qobuzarr-network-policy
spec:
  podSelector:
    matchLabels:
      app: qobuzarr
  policyTypes:
  - Ingress
  - Egress
  egress:
  - to:
    - namespaceSelector:
        matchLabels:
          name: qobuz-api
    ports:
    - protocol: TCP
      port: 443
```

## 🚨 Security Best Practices

### For Users

1. **Credential Security**:
   - Use strong, unique passwords
   - Enable two-factor authentication on Qobuz account
   - Rotate credentials regularly

2. **Environment Security**:
   - Run with minimal required permissions
   - Use dedicated service accounts
   - Enable security monitoring

3. **Network Security**:
   - Use HTTPS-only communications
   - Configure proper firewall rules
   - Monitor network traffic

### For Developers

1. **Secure Coding**:
   - Validate all inputs
   - Use secure credential handling
   - Implement proper error handling

2. **Testing Security**:
   - Run security unit tests
   - Perform penetration testing
   - Review code for vulnerabilities

3. **Deployment Security**:
   - Use signed assemblies
   - Enable security monitoring
   - Configure proper permissions

## 🔍 Security Auditing

### Security Checklist

- [ ] **Credentials**: Using SecureCredentialManager
- [ ] **Input Validation**: All inputs sanitized
- [ ] **ML Models**: Signatures verified
- [ ] **API Security**: HTTPS enforced
- [ ] **Logging**: Sensitive data masked
- [ ] **Monitoring**: Security events tracked
- [ ] **Updates**: Latest security patches applied

### Security Testing

```bash
# Run security tests
dotnet test --filter \"Category=Security\"

# Validate security configuration
dotnet run --project SecurityValidator -- --config /config/settings.json

# Security scan
dotnet run --project SecurityScanner -- --target /app/plugins/
```

---

*Security is built into every layer of Qobuzarr. For security vulnerabilities or concerns, see [SECURITY.md](../SECURITY.md) or contact <security@richertunes.com>*
