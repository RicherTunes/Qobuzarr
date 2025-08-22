# QobuzApiSharp Investigation Results

## Key Findings from TrevTV's QobuzApiSharp Library

### **Critical Discovery: QobuzApiSharp ONLY Uses `intent=stream`**

Contrary to our theory about trying both `intent=download` and `intent=stream`, **TrevTV's working QobuzApiSharp library hardcodes `intent=stream`** in all cases.

**Source Evidence:**
- **File**: `QobuzApiSharp/Api/Service/Endpoints/QobuzApiService.Track.cs:49`
- **Method**: `GetTrackFileUrl(string trackId, string formatId)`
- **Parameters**: Always includes `{ "intent", "stream" }`

```csharp
Dictionary<string, string> parameters = new Dictionary<string, string>
{
    { "track_id", trackId },
    { "format_id", formatId },
    { "intent", "stream" },          // ← ALWAYS "stream", never "download"
    { "request_ts", timestamp },
    { "request_sig", signature }
};
```

### **How QobuzApiSharp Handles Track Restrictions**

#### **1. Sample Detection:**
QobuzApiSharp uses the `Sample` property in `FileUrl` response:

```csharp
// From TrevTV's Downloader.cs line 108-109:
if (urls.Sample ?? false)
    throw new Exception("Qobuz provided a sample. The user probably does not have access to this quality of track.");
```

#### **2. FileUrl Response Model:**
```csharp
public class FileUrl
{
    [JsonProperty("url")]
    public string Url { get; set; }
    
    [JsonProperty("sample")]
    public bool? Sample { get; set; }        // ← Key field for restriction detection
    
    [JsonProperty("status")]
    public string Status { get; set; }
    
    [JsonProperty("message")]
    public string Message { get; set; }
    
    [JsonProperty("code")]
    public string Code { get; set; }
}
```

#### **3. Error Handling Strategy:**
TrevTV's approach is simple and effective:
1. **Request stream URL with `intent=stream`**
2. **Check if `Sample = true`** → Throw exception with clear message
3. **No complex fallback logic** → Just fail the track

### **Signature Generation (Important Implementation Detail)**

QobuzApiSharp uses the exact signature format we already implement:

```csharp
string dataToSign = String.Concat("trackgetFileUrlformat_id", format_id, "intentstreamtrack_id", track_id, timestamp, app_secret);
```

**Note**: The signature hardcodes `"intentstream"` in the concatenation, confirming they only use `intent=stream`.

### **What This Means for Our "TrackRestrictedByPurchaseCredentials" Issue**

#### **Root Cause Analysis:**
1. **QobuzApiSharp would fail the same way** - TrevTV's library would throw "Qobuz provided a sample" exception
2. **The error is subscription-based** - User doesn't have access to full track at this quality level
3. **This is expected behavior** - Not a bug in our implementation

#### **The Real Problem:**
Our error `TrackRestrictedByPurchaseCredentials` suggests we're getting a **different error response format** than TrevTV's library expects.

**Possible Explanations:**
1. **Different API version/endpoint behavior**
2. **Different app_id/app_secret** giving different error responses  
3. **Our error parsing** might be interpreting the response differently

### **Comparison: Our Implementation vs QobuzApiSharp**

| Aspect | Our Implementation | QobuzApiSharp |
|--------|-------------------|---------------|
| **Intent Parameter** | `{"intent", "stream"}` ✅ | `{"intent", "stream"}` ✅ |
| **Error Detection** | String parsing of restrictions | `FileUrl.Sample` boolean ✅ |
| **Fallback Strategy** | Complex quality fallback | Simple: fail if Sample=true |
| **Error Messages** | "TrackRestrictedByPurchaseCredentials" | "Qobuz provided a sample" |

### **Recommended Action Items**

#### **1. Immediate Fix: Check Sample Property**
Update our `QobuzStreamResponse` model to include `Sample` property and check it:

```csharp
public class QobuzStreamResponse
{
    // ... existing properties ...
    
    [JsonProperty("sample")]
    public bool? Sample { get; set; }
}

// In StreamUrlProvider:
if (streamResponse?.Sample == true)
{
    throw new TrackUnavailableException(trackId, "Track is only available as sample/preview", TrackUnavailableReason.PreviewOnly);
}
```

#### **2. Root Cause Investigation**
- **Compare API responses** between our implementation and QobuzApiSharp
- **Verify we're using the same endpoint**: `/track/getFileUrl`
- **Check if our app_id/app_secret** produces different error formats

#### **3. Simplify Error Handling**
Consider adopting QobuzApiSharp's simpler approach:
- Don't try multiple intent values (stick with `stream`)
- Use boolean flags instead of string parsing for restrictions
- Fail fast on subscription issues

### **Bottom Line**

**Our implementation is correct** - we're using the same parameters as the working QobuzApiSharp library. The "TrackRestrictedByPurchaseCredentials" error is likely a **subscription tier limitation**, and TrevTV's library would fail the same way with a "sample" error.

The difference is in **error response parsing and user messaging**, not the core API approach.