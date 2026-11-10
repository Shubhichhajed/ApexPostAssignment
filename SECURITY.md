# Security Design Documentation

## 1. JWT Bearer Authentication

### Configuration
```csharp
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.TokenValidationParameters = new()
        {
            ValidateAudience = false,
            ValidIssuer = builder.Configuration["Auth:Issuer"],
            ValidateIssuer = true,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Auth:SigningKey"]))
        };
    });
```

### Token Structure
JWT tokens carry the following claims:
- `sub` (subject): User/Courier ID
- `iss` (issuer): "https://apexpost.auth.local"
- `aud` (audience): "apexpost-api"
- `exp` (expiration): Unix timestamp
- `iat` (issued at): Unix timestamp
- Custom claims:
  - `courierId`: The courier's identifier (COURIER-001, etc.)
  - `role`: "Courier" or "Admin"

### Development Token Generation
```bash
# Generate a development JWT token with courier role
dotnet user-jwts create \
  --issuer https://apexpost.auth.local \
  --scope api://apexpost \
  --role Courier \
  --claim courierId=COURIER-001
```

Token is stored in `~/.microsoft/usersecrets/<ProjectGuid>/user-secrets.json` and loaded via `IConfiguration`.

### Token Validation
- **Issuer**: Must match configured issuer
- **Lifetime**: Tokens are valid for 1 hour (configurable)
- **Signature**: Verified using configured signing key
- **Audience**: Not validated (accepts any audience)

## 2. Authorization & Access Control

### Endpoint-Level Authorization

#### Public (No Auth Required)
- None in v1/v2

#### Courier-Only (Role: Courier)
- `PATCH /api/v2/shipments/{trackingNumber}/status` - Update shipment status
- `POST /api/v2/shipments/{trackingNumber}/scans` - Upload offline scans

**Additional Check**: Courier can only upload scans with `courierId` matching their own claims:
```csharp
[Authorize(Roles = "Courier")]
public async Task UploadScans(string trackingNumber, ScanUploadRequest request)
{
    var courierIdClaim = User.FindFirst("courierId")?.Value;

    foreach (var scan in request.Scans)
    {
        if (scan.CourierId != courierIdClaim)
        {
            return Forbid(); // 403 Forbidden
        }
    }
    // ... proceed with upload
}
```

#### Admin-Only (Role: Admin)
- `POST /api/v2/shipments` - Create shipment
- `PUT /api/v2/shipments/{trackingNumber}` - Update shipment
- `DELETE /api/v2/shipments/{trackingNumber}` - Delete shipment

#### All Authenticated Users
- `GET /api/v2/shipments` - List shipments
- `GET /api/v2/shipments/{trackingNumber}` - Get single shipment
- `GET /api/v2/shipments/{trackingNumber}/scans` - Get scan history

### Error Responses

**401 Unauthorized** (Missing/Invalid Token):
```json
{
  "type": "https://tools.ietf.org/html/rfc7235#section-3.1",
  "title": "Unauthorized",
  "status": 401,
  "detail": "Authorization header missing, malformed, or token expired"
}
```

**403 Forbidden** (Wrong Role/CourierId):
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.3",
  "title": "Forbidden",
  "status": 403,
  "detail": "Courier COURIER-002 cannot upload scans under COURIER-001"
}
```

## 3. Partner API Keys

### Endpoint
```
GET /api/v2/partner/shipments/{trackingNumber}
Header: X-Api-Key: sk_live_example_key_12345
```

### Implementation
```csharp
public class ApiKeyValidationFilter : IAsyncAuthorizationFilter
{
    private readonly IConfiguration _config;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var apiKey = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
        var validKeys = _config.GetSection("ApiKeys:Partner").Get<List<string>>();

        if (string.IsNullOrEmpty(apiKey) || !validKeys.Contains(apiKey))
        {
            context.Result = new UnauthorizedResult(); // 401
        }
    }
}

[HttpGet("partner/shipments/{trackingNumber}")]
[ServiceFilter(typeof(ApiKeyValidationFilter))]
public async Task<ActionResult> GetShipmentForPartner(string trackingNumber)
{
    // Partner has read-only access
    // No role-based authorization needed
}
```

### Key Management
- **Storage**: User Secrets (Development) or Azure Key Vault (Production)
- **Never**: Hard-coded in source, committed to repository, or logged
- **Rotation**: Keys can be rotated by removing old key and adding new one
- **Expiration**: Consider implementing key expiration dates in metadata

**Configuration Example** (`secrets.json`):
```json
{
  "ApiKeys": {
    "Partner": [
      "sk_live_partner_acme_12345",
      "sk_live_partner_logistics_67890"
    ]
  }
}
```

## 4. OAuth2 Flows & Concepts

### Authorization Code + PKCE (For Mobile Apps)

**Participants**:
1. **Resource Owner**: The courier (uses mobile app)
2. **Client**: ApexPost MAUI mobile app
3. **Authorization Server**: Identity provider (e.g., Azure AD, Auth0)
4. **Resource Server**: ApexPost.Api

**Flow**:
```
1. Courier opens MAUI app
2. App detects: No token in SecureStorage
3. App generates: code_challenge = Base64URL(SHA256(code_verifier))
4. App redirects: Opens browser to Authorization Server
   GET /authorize?
     response_type=code
     client_id=mobile_app_id
     redirect_uri=apexpost://callback
     code_challenge=Base64URL(SHA256(...))
     code_challenge_method=S256
     scope=openid profile shipments:read shipments:write
5. Courier logs in (username/password, MFA)
6. Authorization Server redirects: apexpost://callback?code=auth_code_xyz
7. App exchanges code for token (backend):
   POST /token
     grant_type=authorization_code
     code=auth_code_xyz
     code_verifier=original_random_string
     client_id=mobile_app_id
     client_secret=xxxxxxxxx (from secure backend)
8. Authorization Server returns:
   {
     "access_token": "eyJhbGc...",
     "refresh_token": "ref_xyz",
     "expires_in": 3600,
     "token_type": "Bearer"
   }
9. App stores token in SecureStorage
10. App uses token in Authorization header for API calls
```

**Why PKCE?**
- Prevents authorization code interception on mobile devices
- Even if an attacker intercepts the `code`, they cannot exchange it without the `code_verifier`
- Required for native/mobile apps per OAuth2 Best Current Practice

### Client Credentials Flow (For Backend Services)

**Participants**:
1. **Client**: Partner's backend service (e.g., logistics partner's warehouse system)
2. **Authorization Server**: Identity provider
3. **Resource Server**: ApexPost.Api

**Flow**:
```
1. Partner system (no user context)
2. Requests token with its own credentials:
   POST /token
     grant_type=client_credentials
     client_id=partner_warehouse_system
     client_secret=partner_secret_key
     scope=shipments:read
3. Authorization Server returns token:
   {
     "access_token": "eyJhbGc...",
     "expires_in": 3600,
     "token_type": "Bearer"
   }
4. Partner system uses token for API calls
```

**Use Case**: Read-only access to shipment data for integration with warehouse management system.

### Token Types

**Access Token (JWT)**:
- Short-lived (1 hour)
- Contains claims: user ID, roles, permissions
- Used in `Authorization: Bearer` header
- Sent with every API request

**Refresh Token**:
- Long-lived (e.g., 30 days)
- Not sent to API
- Stored securely on client
- Used to obtain new access tokens when expired

**Example Refresh**:
```csharp
// Client detects 401 Unauthorized on API call
// Uses refresh token to get new access token
var newToken = await identityProvider.RefreshTokenAsync(refreshToken);
secureStorage.Store("access_token", newToken);

// Retry API call with new token
```

## 5. API Key vs. OAuth2

| Aspect | API Key | OAuth2 + JWT |
|--------|---------|-------------|
| **Lifetime** | Long (doesn't expire) | Short (1 hour) + refresh token |
| **User Context** | No - identifies application only | Yes - includes user/courier ID |
| **Granular Permissions** | Limited (all-or-nothing) | Yes - scopes (shipments:read, shipments:write) |
| **Audit Trail** | Weak - doesn't identify who ran what | Strong - knows which courier performed action |
| **Credential Loss** | Compromise affects entire application | Compromise affects only for 1 hour |
| **Revocation** | Manual - remove from config | Automatic - expires after TTL |
| **Refresh Capability** | No - need to rotate entire key | Yes - use refresh token |
| **User Delegation** | Not possible | Possible - user authorizes on behalf of app |
| **Standards** | Custom/proprietary | RFC 6749, RFC 6750, RFC 7231 |

**Recommendation**:
- **Use API Keys for**: Read-only partner access to specific endpoints (e.g., track public shipments)
- **Use OAuth2 for**: Interactive mobile/web apps where user identity matters
- **Use JWT for**: Courier app authentication and audit logging

## 6. Transport Layer Security (HTTPS/TLS)

### Configuration
```csharp
// Enforce HTTPS in all environments
app.UseHttpsRedirection();

// Add HSTS (HTTP Strict-Transport-Security) - not in Development
if (!app.Environment.IsDevelopment())
{
    app.UseHsts(); // Enforces HTTPS for 1 year, includes subdomains
}
```

### Certificate Management
**Development**:
```bash
# Create self-signed certificate
dotnet dev-certs https --trust
# Certificate stored in: ~/.dotnet/corefx/cryptography/x509stores/my/
```

**Production**:
- Use real CA-signed certificate (e.g., from LetsEncrypt, DigiCert)
- Certificate installed on IIS/Kestrel server
- Auto-renewal via ACME client

### Why Tokens/Keys Must Use HTTPS

**Plain HTTP**:
- Bearer token `Authorization: Bearer eyJhbGc...` sent over plain HTTP
- Attacker on network intercepts token
- Attacker uses token to impersonate courier
- Attacker uploads scans, modifies shipments, accesses data

**HTTPS/TLS**:
- All traffic encrypted with TLS 1.2+
- Token encrypted in-transit
- Attacker cannot intercept without breaking encryption
- Certificate pinning (optional): Mobile app only trusts specific certificate

### HSTS Header
```
Strict-Transport-Security: max-age=31536000; includeSubDomains; preload
```
- Forces browser to always use HTTPS for this domain
- Prevents downgrade attacks (HTTP → HTTPS redirect interception)
- Preload: Include domain in HSTS preload list

### Certificate Validation in MAUI

**Development** (Debug mode only):
```csharp
#if DEBUG
    // Allow self-signed certificate in debug for localhost
    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
    {
        if (message.RequestUri?.Host == "localhost" && errors == SslPolicyErrors.RemoteCertificateNotAvailable)
            return true;
        return errors == SslPolicyErrors.None;
    };
#else
    // Production: Validate certificate normally (will reject self-signed)
#endif
```

**Certificate Pinning** (Optional Advanced Security):
```csharp
// Pin specific certificate to prevent MITM attacks
var pinningHandler = new HttpClientHandler();
pinningHandler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
{
    // Check certificate fingerprint against known pinned certificate
    var thumbprint = cert.Thumbprint;
    return thumbprint == "PINNED_CERT_THUMBPRINT_HERE";
};
```

## 7. Migrating to Real Identity Provider

### From Development Tokens to Azure AD

**Step 1**: Register app in Azure AD
```
Application Name: ApexPost Courier
Supported account types: Single tenant
Redirect URI: https://apexpost.example.com/callback
Client Secret: [auto-generated]
```

**Step 2**: Update Program.cs
```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(options =>
    {
        builder.Configuration.Bind("AzureAd", options);
    },
    options =>
    {
        builder.Configuration.Bind("AzureAd", options);
    });
```

**Step 3**: Update appsettings.json
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "tenant-id-here",
    "ClientId": "client-id-here",
    "ClientSecret": "secret-here",
    "Audience": "api://apexpost"
  }
}
```

**Step 4**: MAUI app uses MSAL (Microsoft Authentication Library)
```csharp
var publicClientApp = PublicClientApplicationBuilder
    .Create("client-id")
    .WithRedirectUri("msauth://com.apexpost.courier/...")
    .WithAuthority(AzureCloudInstance.AzurePublic, "tenant-id")
    .Build();

var result = await publicClientApp.AcquireTokenInteractive(scopes)
    .ExecuteAsync();

secureStorage.Store("access_token", result.AccessToken);
```

---

## Summary

- **JWT Tokens**: Short-lived, user-identified, audit-friendly
- **API Keys**: Long-lived, application-identified, simple
- **HTTPS/TLS**: Mandatory for protecting credentials in-transit
- **HSTS**: Prevents downgrade attacks
- **PKCE**: Required for mobile/native apps
- **Real IdP**: Azure AD, Auth0, or similar for production
