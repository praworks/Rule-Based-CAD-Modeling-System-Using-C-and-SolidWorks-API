# Groq Free Tier Protection System

## Overview
Comprehensive rate limiting system to prevent overloading Groq's free tier API limits and avoid account suspension.

## Protection Layers

### 1. **Local Rate Limiter** ([GroqRateLimiter.cs](d:/SolidWorks%20Project/Rule-Based-CAD-Modeling-System-Using-C-and-SolidWorks-API/Services/GroqRateLimiter.cs))
Enforces conservative limits before requests reach the API:

| Limit Type | Default | API Limit | Safety Margin |
|------------|---------|-----------|---------------|
| Per Minute | 20 req/min | 30 req/min | 33% buffer |
| Per Hour | 300 req/hr | ~360 req/hr | 17% buffer |
| Per Day | 12,000 req/day | 14,400 req/day | 17% buffer |
| Min Delay | 2 seconds | N/A | Anti-burst |

### 2. **Automatic Blocking**
Requests are **blocked before** hitting the API if:
- Too many requests in rolling 1-minute window
- Too many requests in rolling 1-hour window
- Daily quota exceeded
- Less than 2 seconds since last request (burst protection)

### 3. **Server-Side Respect** ([GroqClient.cs](d:/SolidWorks%20Project/Rule-Based-CAD-Modeling-System-Using-C-and-SolidWorks-API/Services/GroqClient.cs))
Existing protection:
- Reads `x-ratelimit-remaining-requests` header
- Reads `x-ratelimit-remaining-tokens` header
- Honors `retry-after` on 429 responses
- Exponential backoff on rate limit errors
- Refuses requests when API reports low remaining quota

## User Interface

### Settings Window
**Location**: Settings → AI Provider Settings → Groq Configuration

**New Features**:
- 📊 **Real-time Usage Display**: Shows current usage stats
  - Requests in last minute
  - Requests in last hour
  - Requests in last day
- ⚡ **Rate Limit Info Box**: Green box showing:
  - Current usage: "Groq usage: 5/20 per min, 42/300 per hour, 156/12,000 per day"
  - Configured limits
- 🔄 **Reset Button**: Clear tracking history (for troubleshooting)

### Error Messages
When rate limit is hit, users see:
```
⚠️ [GROQ RATE LIMIT] Minute limit reached: 20/20 requests in last 60s. Wait 1 minute. (Wait 60s)
💡 Tip: Using Groq free tier - try Local LM or Gemini, or wait before retrying.
📊 Groq usage: 20/20 per min, 156/300 per hour, 1230/12,000 per day
```

## Configuration

### Environment Variables (Optional)
Override default limits:

```
GROQ_MAX_PER_MINUTE=20        # Requests per minute
GROQ_MAX_PER_HOUR=300         # Requests per hour
GROQ_MAX_PER_DAY=12000        # Requests per day
GROQ_MIN_DELAY_SECONDS=2.0    # Minimum seconds between requests
```

**Example**: More conservative limits
```powershell
[Environment]::SetEnvironmentVariable("GROQ_MAX_PER_MINUTE", "10", "User")
[Environment]::SetEnvironmentVariable("GROQ_MIN_DELAY_SECONDS", "3", "User")
```

## How It Works

### Request Flow
```
User Prompt
    ↓
[RefinePromptAsync?] → GroqLlmClient.GenerateAsync()
    ↓                           ↓
    |                    GroqRateLimiter.CheckRequest()
    |                           ↓
    |                    [Allowed?] → Yes → API Call → GroqClient.SendAsync()
    |                           ↓                            ↓
    |                    No → Exception              Server Rate Headers
    |                           ↓                            ↓
    |                    "Too fast: Wait 2s"        Record: GroqRateLimiter.RecordRequest()
    ↓
Main CAD Generation → Same flow
```

### Tracking Mechanism
1. **In-Memory History**: Stores timestamp of each request
2. **Rolling Windows**: Automatically cleans up entries older than 24 hours
3. **Thread-Safe**: Uses lock to prevent race conditions
4. **Persistent**: Lives for entire application session

### Fallback Strategy
When Groq is rate limited, the system automatically tries:
1. **Groq** (blocked by limiter)
2. **Next in priority** (Local LM or Gemini)
3. **Final fallback** (remaining provider)

## Protection Scenarios

### Scenario 1: Rapid Clicks
**User**: Clicks "Build" 10 times in 5 seconds
**Result**: 
- 1st request: ✅ Allowed (2s delay = 0s)
- 2nd request (after 2s): ✅ Allowed
- 3rd request (before 2s): ❌ Blocked - "Too fast: Wait 1.5s"

### Scenario 2: Batch Processing
**User**: Runs 25 builds in quick succession
**Result**:
- Builds 1-20: ✅ Allowed (with 2s delays)
- Builds 21-25: ❌ Blocked - "Minute limit reached: 20/20 requests"
- **Auto-fallback**: Switches to Local LM or Gemini

### Scenario 3: Heavy Testing
**User**: Tests 350 prompts in one hour
**Result**:
- Prompts 1-300: ✅ Allowed
- Prompts 301-350: ❌ Blocked - "Hourly limit reached: 300/300 requests in last hour"

### Scenario 4: Daily Usage
**User**: Uses system throughout the day
**Result**:
- Builds 1-12,000: ✅ Allowed
- Build 12,001+: ❌ Blocked - "Daily limit reached: 12,000/12,000 requests today. Try again tomorrow."

## Monitoring

### Check Usage Stats
In Settings window, Groq section shows:
```
Groq usage: 5/20 per min, 42/300 per hour, 156/12,000 per day
Limits: 20/min • 300/hour • 12,000/day • 2s delay between requests
```

### Reset Tracking
If you encounter false positives:
1. Open Settings → AI Provider Settings
2. Scroll to Groq section
3. Click **Reset** button
4. Confirm reset

**When to reset**:
- After API key change
- After long idle period (24+ hours)
- If experiencing false "rate limit" errors
- For testing/debugging

## Code Integration

### Where Rate Limiting Applies
✅ **Protected**:
- Main CAD generation (`GenerateWithFallbackAsync()`)
- Prompt refinement when using Groq
- Settings window "Test" button
- All Groq API calls via `GroqLlmClient`

❌ **Not Protected** (different providers):
- Local LM Studio calls
- Google Gemini calls
- MongoDB operations

### Developer Notes
To add rate limiting to a new Groq feature:
```csharp
// Option 1: Use GroqLlmClient (automatic protection)
using (var client = new GroqLlmClient(apiKey, model))
{
    var result = await client.GenerateAsync(prompt); // Auto-protected
}

// Option 2: Manual check (rare cases)
var check = GroqRateLimiter.CheckRequest();
if (!check.Allowed)
{
    throw new Exception($"Rate limit: {check.Reason}");
}
// ... make API call ...
GroqRateLimiter.RecordRequest(); // Record successful call
```

## Benefits

### For Users
- ✅ **Never** hit Groq's hard rate limits
- ✅ **Automatic** fallback to other providers
- ✅ **Clear** error messages with wait times
- ✅ **Transparent** usage statistics
- ✅ **No account suspension** risk

### For System
- ✅ Prevents API throttling
- ✅ Maintains reliable service
- ✅ Reduces error rates
- ✅ Improves user experience
- ✅ Extends free tier usability

## Troubleshooting

### Issue: "Rate limit" but I haven't used Groq today
**Solution**: 
1. Check if another application is using the same API key
2. Click "Reset" in Settings → Groq section
3. Verify environment variables aren't set too low

### Issue: Too restrictive, want more requests
**Solution**:
```powershell
# Increase limits (stay under API limits!)
[Environment]::SetEnvironmentVariable("GROQ_MAX_PER_MINUTE", "25", "User")
[Environment]::SetEnvironmentVariable("GROQ_MAX_PER_HOUR", "500", "User")
```

### Issue: Want faster burst requests
**Solution**:
```powershell
# Reduce minimum delay (not recommended)
[Environment]::SetEnvironmentVariable("GROQ_MIN_DELAY_SECONDS", "1", "User")
```

### Issue: Rate limiter shows wrong stats
**Solution**:
1. Click "Reset" button
2. Restart SolidWorks
3. Re-check after making 1-2 test requests

## Best Practices

1. **Use Priority Order**: Set Groq as last fallback, Local LM first
2. **Monitor Usage**: Check stats regularly during heavy use
3. **Respect Limits**: Don't disable rate limiting
4. **Test Responsibly**: Use Local LM for testing, Groq for production
5. **Reset Sparingly**: Only reset if genuinely stuck

## Future Enhancements

Potential improvements:
- Persistent tracking across sessions (save to file)
- Per-user quota management for team usage
- Dynamic limit adjustment based on API response headers
- Usage analytics and reporting
- Quota warnings at 80% threshold
- Time-based auto-reset (e.g., at midnight)
