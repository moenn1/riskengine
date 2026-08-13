# ASP.NET Core authentication and authorization deep dive

This project demonstrates the security pipeline with JWT bearer tokens and
policy-based authorization. The local token endpoint is deliberately a learning
shortcut; it is not a production identity system.

## Authentication versus authorization

Authentication answers **who is calling?** Authorization answers **what may
that identity do?**

```text
Authorization: Bearer <JWT>
        ↓
JwtBearerHandler validates signature/issuer/audience/expiry
        ↓
HttpContext.User (ClaimsPrincipal)
        ↓
Authorize policy checks claims/roles
        ↓
controller action or 401/403
```

Spring comparison:

| Spring Security | ASP.NET Core |
| --- | --- |
| `SecurityFilterChain` | middleware plus registered authentication schemes |
| bearer authentication filter | `JwtBearerHandler` |
| `Authentication` principal | `HttpContext.User` / `ClaimsPrincipal` |
| `@PreAuthorize` | `[Authorize(Policy = "...")]` |
| `application.yml` properties | `IOptions<SecurityOptions>` |

## Code tour

`Program.cs` registers `AddAuthentication().AddJwtBearer(...)`. The
`TokenValidationParameters` require a valid issuer, audience, signing key, and
lifetime. `UseAuthentication()` must run before `UseAuthorization()` and before
protected endpoints execute.

`Security/SecurityOptions.cs` binds configuration. In this sample the HMAC key
is a development placeholder and startup validation requires at least 32 bytes.
Real deployments should validate tokens from an OIDC provider using rotating
public keys, with secrets supplied by a secret manager or environment.

`AuthController` exposes `POST /api/v1/auth/token` only in Development/Testing.
It accepts a demo role so learners can observe permissions. It must be removed
or disabled in production because a real user cannot choose their own role.

`PortfoliosController` uses:

```csharp
[Authorize(Policy = "RiskReader")]
```

and portfolio creation adds:

```csharp
[Authorize(Policy = "RiskOperator")]
```

The policies accept `risk-reader`/`risk-operator` roles. Missing or invalid
credentials produce **401 Unauthorized**; valid credentials without the required
role produce **403 Forbidden**.

## Browser flow

`/login.html` is a separate page. The workbench redirects there when no session
token exists, so protected content is not briefly rendered before login. The
login page stores the short-lived demo token in `sessionStorage`, and the
workbench adds it to `getJson`/`sendJson`. Sign out clears the session and
redirects back to the login page. This is still only a learning flow; production
browser applications normally use the OAuth2
authorization-code flow with PKCE and an approved identity provider.

## Security boundaries to add next

- replace demo JWT minting with OIDC discovery and key rotation;
- use scopes/permissions for business actions and claims for identity context;
- enforce desk/tenant authorization in the query/use-case, not only in the UI;
- audit actor, action, resource, result, correlation ID, and input version;
- require HTTPS and configure trusted forwarded headers correctly;
- configure restrictive CORS only when a separate origin is required;
- protect cookie-based browser flows against CSRF (bearer headers have a different boundary);
- redact tokens, positions, and secrets from logs;
- rate-limit token and calculation endpoints separately;
- test both 401 and 403 at the HTTP boundary;
- rotate signing keys with overlapping validation keys and short token lifetimes.

The security goal is not “add an attribute.” It is a chain from identity
provider, token validation, policy decision, resource-level authorization,
auditing, and operational key management.
