# Deep dive: browser UI, static files, and the API boundary

This chapter explains every browser-side component added to the risk engine,
how ASP.NET Core serves it, how it calls the existing API, and how the design
compares with a Java/Spring Boot application.

Read it with:

- [`Program.cs`](../src/TradingRisk.Api/Program.cs);
- [`index.html`](../src/TradingRisk.Api/wwwroot/index.html);
- [`site.css`](../src/TradingRisk.Api/wwwroot/css/site.css);
- [`app.js`](../src/TradingRisk.Api/wwwroot/js/app.js);
- [`PortfolioApiTests.cs`](../tests/TradingRisk.Tests/Api/PortfolioApiTests.cs);
  and
- the [browser UI Mermaid sequence](06-mermaid-diagrams.md#7-browser-ui-flow).

The UI has no separate framework, package manager, build process, or server.
That is a deliberate learning choice, not a claim that every large .NET system
should use plain JavaScript.

## 1. What the UI is responsible for

The browser workbench performs adapter responsibilities:

1. collect a portfolio name, currency, and signed positions;
2. serialize those values to the API's create-portfolio JSON shape;
3. display the portfolio returned by the server;
4. construct a scenario-return matrix for the returned instruments;
5. serialize the matrix to the calculate-risk JSON shape;
6. display the returned risk report and scenario P&L distribution;
7. translate HTTP/Problem Details failures into readable feedback; and
8. show whether the API health endpoint responds.

It does **not**:

- calculate market value, VaR, ES, worst loss, or volatility;
- validate all domain invariants;
- access `TradingRisk.Domain` types;
- store portfolios durably;
- decide the quantile convention; or
- become authoritative merely because it performs HTML input validation.

The server remains the trusted boundary. Browser code can be bypassed or
modified by its user, so every business rule still belongs in Application or
Domain and every HTTP rule still belongs in API.

This is the same principle used in a Spring system: a React, Angular, Thymeleaf,
or plain-JavaScript form may offer immediate feedback, but Bean Validation and
domain validation still run on the server.

## 2. Files and their runtime roles

| File | Build-time role | Runtime role |
|---|---|---|
| `wwwroot/index.html` | Copied to publish output by the Web SDK | Initial document and semantic UI structure |
| `wwwroot/css/site.css` | Copied as content | Layout, typography, colors, responsive behavior, focus states, and chart presentation |
| `wwwroot/js/app.js` | Copied as content | State, DOM construction, event handling, API calls, error handling, and report rendering |
| `Program.cs` | Compiled into `TradingRisk.Api.dll` | Adds default-file and static-file middleware |
| `PortfolioApiTests.cs` | Compiled into the test assembly | Proves `/`, CSS, and JavaScript are served through the real ASP.NET Core pipeline |

There is no `package.json`, `node_modules`, webpack, Vite, npm, or frontend
dependency. The browser natively understands all three asset types.

### Java/Spring comparison

| Spring Boot | ASP.NET Core here |
|---|---|
| `src/main/resources/static/index.html` | `wwwroot/index.html` |
| static resource handler | static-file middleware |
| `index.html` welcome page convention | default-file middleware rewriting `/` to `/index.html` |
| Maven/Gradle resource copying | Web SDK content publishing |
| `fetch("/api/...")` from the page | the same browser `fetch("/api/...")` |
| Jackson JSON naming defaults | `System.Text.Json` web naming defaults |

The browser technology is not Java-specific or .NET-specific. The difference
is how the server locates, packages, and serves the files.

## 3. Why the files live under `wwwroot`

ASP.NET Core calls the application directory its **content root**. By default,
the **web root** is a `wwwroot` directory beneath it. Files under the web root
are candidates for public static serving; ordinary source and configuration
files outside it are not.

The API project uses:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
```

`Microsoft.NET.Sdk.Web` supplies web project defaults. One of those conventions
is treating `wwwroot` files as content that belongs in publish output. There is
no explicit item such as this in the project:

```xml
<Content Include="wwwroot/**" CopyToPublishDirectory="PreserveNewest" />
```

because the Web SDK already supplies the conventional item rules. Adding an
unnecessary duplicate item can make MSBuild report duplicate content.

This differs from a plain class library using:

```xml
<Project Sdk="Microsoft.NET.Sdk">
```

A class library has no reason to assume it hosts a public web root.

Microsoft's
[ASP.NET Core fundamentals](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/?view=aspnetcore-10.0)
describe the content root and default `wwwroot` web root. The
[static-files documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/static-files?view=aspnetcore-10.0)
explains static-asset serving and current alternatives.

### Security consequence

Do not put secrets, private keys, source maps containing sensitive source,
internal documents, or production configuration under `wwwroot`. If a matching
static-file request is allowed, those files are designed to be downloadable.

`appsettings.json` is content used by the host, but it is outside `wwwroot`;
the static-file middleware does not expose it by convention.

## 4. The two middleware calls

The relevant part of `Program.cs` is:

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();
```

These calls add two different middleware components.

### 4.1 `UseDefaultFiles`

For a request such as:

```text
GET /
```

default-file middleware looks for a conventional file such as `index.html`.
When it finds one, it rewrites the request path to:

```text
/index.html
```

It does not send the file body itself. It changes the request so later
middleware can handle it.

### 4.2 `UseStaticFiles`

Static-file middleware examines the rewritten path, maps it beneath the web
root, determines the content type, and writes the file response. Therefore the
pair behaves conceptually like:

```text
GET /
  -> DefaultFiles: path becomes /index.html
  -> StaticFiles: serve wwwroot/index.html as text/html
```

Direct asset requests skip the useful rewrite but are still served:

```text
GET /css/site.css -> wwwroot/css/site.css
GET /js/app.js     -> wwwroot/js/app.js
```

`DefaultFilesMiddleware` and `StaticFileMiddleware` are documented separately
in the
[ASP.NET Core API reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.defaultfilesextensions.usedefaultfiles).

### 4.3 Why order matters

Middleware executes in registration order for a request. `UseDefaultFiles`
must run before `UseStaticFiles`; otherwise the static middleware sees `/`
rather than `/index.html`.

The static pipeline appears before rate limiting:

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
```

Therefore CSS and JavaScript are not assigned the controller rate-limiting
policy. The policy is attached explicitly here:

```csharp
app.MapControllers().RequireRateLimiting("api");
```

The UI still uses rate-limited controller endpoints.

### 4.4 Why no controller returns the page

A controller would be useful if server code had to select a view or populate a
model. This page is a fixed application shell; its changing data arrives from
JSON endpoints. Serving it as a static asset avoids a controller action whose
only job is reading one fixed file.

The Spring comparison is the difference between a static welcome page and a
Thymeleaf MVC view returned by `@Controller`.

## 5. How the application starts with the UI

Adding `wwwroot` does not create a second process. Startup is still:

1. the operating system starts `dotnet TradingRisk.Api.dll`;
2. generated `Main` executes the top-level statements in `Program.cs`;
3. `WebApplication.CreateBuilder(args)` creates host configuration;
4. services are registered in `builder.Services`;
5. `builder.Build()` creates the application and root service provider;
6. middleware and endpoints are registered;
7. `app.Run()` starts Kestrel and waits for requests.

Only after a browser requests `/` do the static-file components read and return
the UI assets.

### Spring Boot equivalent

The rough comparison is:

```java
public static void main(String[] args) {
    SpringApplication.run(RiskApplication.class, args);
}
```

The embedded server starts one application. A static resource mapping inside
that application serves the page; REST controllers in the same process serve
JSON. Neither platform needs a Node server for already-built static assets.

## 6. Same-origin architecture

The browser opens:

```text
http://localhost:5229/
```

JavaScript then calls relative URLs:

```javascript
fetch("/health")
fetch("/api/v1/portfolios", ...)
fetch(`/api/v1/portfolios/${portfolioId}/risk`, ...)
```

A leading slash keeps the current scheme, host, and port. The browser resolves
the first API request to:

```text
http://localhost:5229/api/v1/portfolios
```

The page and API therefore have the same origin.

An origin is the combination of scheme, host, and port. These are different
origins:

```text
http://localhost:5173
http://localhost:5229
```

That is why a separately hosted Vite/React development server usually needs a
proxy or an ASP.NET Core CORS policy. This project needs neither for its normal
flow. See Microsoft's
[CORS guidance](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0)
for the browser security model.

Same-origin deployment is operationally simple:

- one server/process;
- one base URL;
- one TLS certificate and reverse-proxy route;
- no duplicated API base URL in browser configuration; and
- no CORS policy to keep synchronized.

It does not remove the need for authentication, authorization, CSRF analysis,
secure cookies, input validation, or TLS in a production system.

## 7. HTML: structure before appearance

`index.html` contains the information hierarchy and native controls. CSS can
change its appearance; JavaScript can change its state. Keeping a meaningful
document structure makes the page more robust when styles load late, scripts
fail, or assistive technology reads it.

### 7.1 Document head

```html
<!doctype html>
<html lang="en">
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="stylesheet" href="/css/site.css">
<script type="module" src="/js/app.js"></script>
```

- `<!doctype html>` selects standards mode.
- `lang="en"` gives assistive technology the document language.
- UTF-8 permits the symbols used in labels.
- the viewport declaration lets mobile browsers use device width.
- root-relative asset paths work at `/` and do not depend on the current route.
- `type="module"` defers execution until parsing finishes and gives the script
  module semantics.

No inline event attributes such as `onclick="..."` are used. Event behavior is
attached in JavaScript, keeping the HTML focused on structure.

### 7.2 Semantic regions

The page uses:

- `header` for application identity and API status;
- `main` for the unique page content;
- `section` for workflow/report/concept regions;
- `article` for self-contained panels and metric cards;
- `aside` for methodology and interpretation;
- `footer` for safety and operational links;
- `ol` for the ordered learning journey; and
- real `form`, `fieldset`, `legend`, `label`, `table`, `th`, and `caption`
  elements for input and tabular data.

A generic `<div>` is still appropriate when no semantic element describes a
layout wrapper.

### 7.3 Native form behavior

Inputs use attributes such as:

```html
<input type="number" min="50" max="99.99" step="0.01" required>
```

The browser can prevent obviously incomplete submissions and present suitable
mobile controls. That is usability validation, not a security boundary.

The `submit` event is handled on the `<form>`, not only on button clicks. This
preserves keyboard Enter submission and standard form semantics.

### 7.4 Dynamic input labels

Position and scenario controls are created in JavaScript. Every dynamic input
receives either a connected `<label>` or an `aria-label`. Remove buttons have
labels such as “Remove scenario 4,” while their visual text remains the compact
`×` symbol.

### 7.5 Status, errors, and generated graphics

The API indicator and toast use polite live regions:

```html
<div aria-live="polite">...</div>
<div role="status" aria-live="polite">...</div>
```

The error panel uses `role="alert"` because failed submissions need more urgent
announcement.

The P&L chart is visual, but its complete scenario/value description is also
written into a visually hidden paragraph and connected with
`aria-labelledby`. The result table presents the same data in a non-graphical
form.

### 7.6 Hidden state

The native `hidden` attribute is used for states that should be absent from
layout and the accessibility tree:

```html
<form id="risk-form" hidden>
<section id="risk-report" hidden>
```

JavaScript changes the Boolean property:

```javascript
elements.riskForm.hidden = false;
```

Using the property is clearer than constructing `style="display: none"` strings.

## 8. CSS: a small design system

The stylesheet is intentionally plain CSS so the browser, cascade, and
responsive mechanics remain visible.

### 8.1 Custom properties are design tokens

The `:root` block defines repeated decisions:

```css
:root {
  --ink: #10231f;
  --paper: #f5f2e9;
  --accent: #ef9f63;
  --radius-lg: 1.5rem;
}
```

A custom property is resolved by the browser at runtime. It is not the same as
a Sass variable, which is replaced during a preprocessing build.

This resembles central theme values in a Java UI system, but CSS tokens
cascade: a subtree can override a property and its descendants inherit it.

### 8.2 Layout tools

The UI primarily uses:

- CSS Grid for workbench panels, metric cards, and report areas;
- Flexbox for one-dimensional alignment such as headers and button rows;
- `minmax()` and fractional units for fluid columns;
- `clamp()` for values that scale within safe minimum/maximum bounds; and
- `overflow-x: auto` around wide tables.

Grid describes rows and columns together. Flexbox describes distribution along
one main axis. Choosing by layout shape is more useful than treating one as a
newer replacement for the other.

### 8.3 Responsive behavior

Media queries collapse multi-column arrangements and adjust padding when the
viewport narrows. The HTML remains the same document; only presentation
changes.

The scenario table can have a column for every instrument, so it lives in a
scroll container rather than shrinking numeric inputs until they are unusable.

### 8.4 Focus and keyboard visibility

Interactive controls have a visible `:focus-visible` treatment. Do not remove
browser outlines without supplying a replacement: keyboard users need to know
which control will receive the next action.

### 8.5 Reduced motion

CSS and JavaScript both respect:

```css
@media (prefers-reduced-motion: reduce) { ... }
```

and:

```javascript
window.matchMedia("(prefers-reduced-motion: reduce)").matches
```

The browser then avoids smooth scrolling and nonessential motion when the user
has expressed that preference.

### 8.6 The chart is output, not finance logic

The chart bars are DOM elements. Their height is normalized against the largest
absolute returned P&L:

```javascript
Math.abs(result.profitAndLoss) / maximumAbsolutePnl * 43
```

This only maps already-calculated values to pixels/percentages. It does not
derive portfolio P&L or a risk metric. If the chart changes, the server report
and finance tests are unaffected.

For a production analytical chart, a reviewed charting library may provide
axes, zoom, large-dataset performance, and richer accessibility. It would also
add a dependency, security updates, bundle work, and an API to learn.

## 9. JavaScript structure and syntax

The script uses modern browser JavaScript, not C#. Some syntax looks similar,
but the runtime and type guarantees are different.

### 9.1 State

```javascript
const state = {
  portfolio: null,
  positionRowCounter: 0,
  scenarioRowCounter: 0
};
```

`const` prevents reassigning the `state` binding. It does **not** make the
object immutable; `state.portfolio = portfolio` remains valid.

C# comparison:

```csharp
var state = new UiState();
state.Portfolio = portfolio;
```

Java comparison:

```java
final var state = new UiState();
state.setPortfolio(portfolio);
```

In all three examples, the reference/binding is stable while the referenced
object may still be mutable.

Browser state is deliberately small. The authoritative portfolio is returned
by the API and also stored in the server repository. Reloading the page clears
browser state; restarting the process does not clear the portfolio because the
server repository commits it to SQLite. The UI currently does not automatically
reload a prior portfolio into its page state.

### 9.2 Cached DOM references

```javascript
const elements = {
  portfolioForm: document.querySelector("#portfolio-form"),
  riskForm: document.querySelector("#risk-form")
};
```

Selectors are evaluated once during module initialization, then functions reuse
the element references. TypeScript could add compile-time null/type checking;
plain JavaScript relies on the HTML IDs remaining synchronized with this file.
The integration test proves assets are served, but not that every selector
matches.

### 9.3 Functions and default parameters

```javascript
function fillPortfolioForm(portfolio, shouldFocus = true) {
  // ...
}
```

JavaScript supports default parameter expressions directly. C# also supports
optional parameters when the default is a compile-time constant:

```csharp
void FillPortfolioForm(Portfolio portfolio, bool shouldFocus = true)
```

Java normally uses overloads or explicit arguments.

### 9.4 Arrays, mapping, and object literals

```javascript
const positions = [...rowElements].map(row => ({
  instrumentId: row.querySelector('[name="instrumentId"]').value.trim(),
  quantity: Number(row.querySelector('[name="quantity"]').value),
  price: Number(row.querySelector('[name="price"]').value)
}));
```

- `[...iterable]` materializes an iterable as an array;
- `.map(...)` projects each item;
- `row => (...)` is an arrow function; and
- `{ instrumentId, quantity, price }`-shaped values are ordinary dynamic
  objects.

Similar C#:

```csharp
var positions = rows
    .Select(row => new CreatePositionRequest(
        InstrumentId: ReadInstrument(row),
        Quantity: ReadQuantity(row),
        Price: ReadPrice(row)))
    .ToArray();
```

Similar Java:

```java
var positions = rows.stream()
    .map(row -> new CreatePositionRequest(
        readInstrument(row),
        readQuantity(row),
        readPrice(row)))
    .toList();
```

C# and Java examples construct declared record/class types. Plain JavaScript
constructs a runtime object with no compiler-checked DTO contract.

### 9.5 DOM construction

Dynamic rows are created with:

```javascript
const input = document.createElement("input");
input.name = "quantity";
input.type = "number";
row.append(input);
```

This avoids large HTML strings and lets the code set properties and event
listeners directly.

Most importantly, user/API values are written with:

```javascript
element.textContent = text;
```

They are not assigned to `innerHTML`. `textContent` creates text, so a portfolio
name resembling markup is displayed rather than interpreted as markup. MDN's
[`textContent` reference](https://developer.mozilla.org/en-US/docs/Web/API/Node/textContent)
explains the property.

That is one XSS defense, not a complete application security program. New code
must preserve the distinction, and production systems should add suitable
Content Security Policy, authentication/authorization, dependency review, and
security testing.

## 10. Events and the UI state machine

Listeners are registered once at the bottom of the module:

```javascript
elements.portfolioForm.addEventListener("submit", submitPortfolio);
elements.riskForm.addEventListener("submit", submitRisk);
```

The broad UI sequence is:

```text
initial
  -> portfolio form populated with a guided example
  -> submit portfolio
  -> active portfolio summary
  -> scenario form unlocked and shaped by instruments
  -> submit risk request
  -> report, chart, interpretation, and table rendered
```

`event.preventDefault()` stops the browser's traditional form navigation. The
handler sends JSON asynchronously and updates the current document instead.

Buttons are disabled while a request is active. This improves feedback and
reduces accidental double submission, but it is not server-side idempotency.
Two clients, two tabs, or a scripted caller can still submit the same action.
A production write API needs an explicit idempotency/concurrency policy where
the business operation requires one.

### Editing versus updating

“Create another” resets the client form and creates a new portfolio. It does
not edit or delete the previous server object. That wording matches the API,
which currently has `POST` and `GET` but no `PUT`, `PATCH`, or `DELETE`.

## 11. Calling ASP.NET Core with `fetch`

The central adapter function is:

```javascript
async function sendJson(url, method, payload) {
  const response = await fetch(url, {
    method,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json"
    },
    body: JSON.stringify(payload)
  });

  const body = await response.json().catch(() => null);

  if (!response.ok) {
    // map HTTP failure
  }

  return body;
}
```

MDN's [Fetch guide](https://developer.mozilla.org/en-US/docs/Web/API/Fetch_API/Using_Fetch)
is the primary browser reference.

### 11.1 `async` and `await`

`fetch` returns a JavaScript `Promise`. An `async` function also returns a
Promise, and `await` suspends that function until the Promise settles without
blocking the browser event loop.

Platform comparison:

| JavaScript | C# | Java |
|---|---|---|
| `Promise<Response>` | `Task<HttpResponseMessage>` | often `CompletableFuture<HttpResponse<T>>` for async clients |
| `await promise` | `await task` | `.thenApply(...)`, `.join()`, or framework-specific coroutine/reactive syntax |
| `try/catch` around `await` | `try/catch` around `await` | exception handling depends on async abstraction |

The similar keyword does not make JavaScript `Promise` and .NET `Task` the same
type or scheduling model.

### 11.2 Headers and JSON

`Content-Type: application/json` says the request body is JSON. `Accept:
application/json` states the preferred response representation.

`JSON.stringify` turns the JavaScript object into text. ASP.NET Core model
binding and `System.Text.Json` turn that text into `CreatePortfolioRequest` or
`CalculateRiskRequest`.

### 11.3 C# PascalCase and JSON camelCase

The C# contract declares:

```csharp
public sealed record CreatePortfolioRequest(
    string Name,
    string BaseCurrency,
    IReadOnlyList<CreatePositionRequest> Positions);
```

The browser sends:

```json
{
  "name": "Learning Book",
  "baseCurrency": "USD",
  "positions": [
    {
      "instrumentId": "AAPL",
      "quantity": 100,
      "price": 200
    }
  ]
}
```

ASP.NET Core's web JSON defaults use camel case and case-insensitive input
matching. C# follows its PascalCase public-member convention while JSON follows
the common camelCase API convention.

Jackson often provides the same outcome for Java record/component or bean
properties, but naming strategies are configuration: never assume two services
share a convention without checking their serialized contract.

### 11.4 Why `fetch` does not reject for HTTP 400

`fetch` normally rejects for a network-level failure. An HTTP 400, 404, 429, or
500 is still a completed HTTP exchange, so the script checks:

```javascript
if (!response.ok) { ... }
```

This catches the common mistake of treating “the Promise resolved” as “the API
operation succeeded.”

### 11.5 Cancellation and timeouts

The current learning UI does not cancel a submitted request. Production browser
code can pass an `AbortSignal` from `AbortController` to `fetch` and abort when
a view is left or a deadline expires.

ASP.NET Core exposes client disconnect/abort as a `CancellationToken` parameter
to the controller, and this application passes it into asynchronous repository
operations. Cancellation is cooperative on both sides.

## 12. Problem Details end to end

ASP.NET Core emits standardized problem responses. A validation response may
look conceptually like:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Positions": ["The Positions field is required."]
  }
}
```

The browser:

1. parses the response JSON if possible;
2. reads `title`, `detail`, and `errors`;
3. flattens validation error arrays for display;
4. throws an `ApiError` carrying HTTP status and safe details; and
5. renders the error through `textContent`.

This mirrors Spring's use of a stable error DTO or RFC Problem Details support
instead of returning exception stack traces to the browser.

The UI also handles a non-JSON failure by falling back to a generic message.
Reverse proxies and infrastructure can sometimes generate error responses that
do not use the application's JSON schema.

## 13. Numbers, money, percentages, and dates

### 13.1 HTML values become strings

Even `<input type="number">` exposes `.value` as a string. The code explicitly
converts:

```javascript
Number(input.value)
```

JSON has one number syntax. After parsing, ASP.NET Core converts compatible JSON
numbers into C# `decimal` contract properties and rejects invalid shapes.

JavaScript `Number` is IEEE-754 binary floating point. C# `decimal` is a
base-10-oriented 128-bit value type useful for money-like arithmetic. The
browser only transports and formats learning-scale inputs; authoritative
arithmetic remains in C# `decimal`.

For production trading inputs requiring exact decimal lexical preservation,
teams sometimes transport decimals as strings with an explicit schema. That
choice must be applied consistently across OpenAPI, clients, validation, and
storage.

### 13.2 Confidence conversion

The human-facing control accepts `80` for 80%. The API contract expects `0.80`:

```javascript
confidenceLevel: Number(elements.confidenceLevel.value) / 100
```

The report performs the inverse for display. Naming the boundary prevents an
easy factor-of-100 defect.

### 13.3 Locale-aware output

`Intl.NumberFormat` and `Intl.DateTimeFormat` use the browser's locale:

```javascript
new Intl.NumberFormat(undefined, {
  style: "currency",
  currency: "USD"
}).format(value);
```

The API stays culture-neutral JSON; presentation chooses localized separators,
currency symbols, and date labels.

Scenario dates are `YYYY-MM-DD` values with no time zone. The script appends
midnight UTC when constructing a JavaScript `Date` for formatting, preventing a
negative time-zone offset from showing the previous calendar date.

The calculated timestamp is a real UTC instant from .NET `DateTimeOffset` and
is formatted in the browser's local time.

## 14. Risk interpretation shown by the UI

The report deliberately distinguishes:

- VaR: a selected modeled loss threshold at the requested confidence;
- Expected Shortfall: the average modeled loss in the selected tail;
- worst observed loss: the maximum loss in this submitted sample;
- daily P&L volatility: dispersion of the modeled one-period P&L; and
- annualized P&L volatility: daily volatility scaled by the model's annualizing
  assumption.

The interpretation card says VaR is not a maximum-loss guarantee. The scenario
table preserves the individual P&L and loss observations so a learner can audit
the headline metrics.

The browser receives all of these values from `RiskReportDto`. Read
[the finance deep dive](03-risk-metrics.md) for formulas, sign conventions,
sample quantile choices, and limitations.

## 15. Build, run, and package

### Development

```bash
dotnet restore RiskEngine.slnx
dotnet run --project src/TradingRisk.Api --launch-profile http
```

Open:

```text
http://localhost:5229/
```

Unlike a frontend dev server, the browser does not hot-module-reload this page.
Edit a file, refresh the browser, and the ASP.NET Core process serves the
updated asset from the project web root.

The `launchSettings.json` profile supplies local URLs. It is not used as
production hosting configuration.

### Build

```bash
dotnet build RiskEngine.slnx -c Release
```

The C# compiler does not compile HTML, CSS, or JavaScript. The Web SDK tracks
them as content while compiling the application projects.

`node --check src/TradingRisk.Api/wwwroot/js/app.js` can independently parse the
JavaScript if Node is available; it does not execute browser DOM code.

### Publish

```bash
dotnet publish src/TradingRisk.Api/TradingRisk.Api.csproj \
  -c Release \
  -o artifacts/publish
```

The publish layout includes:

```text
artifacts/publish/
├── TradingRisk.Api.dll
├── TradingRisk.Api.deps.json
├── TradingRisk.Api.runtimeconfig.json
├── appsettings.json
└── wwwroot/
    ├── index.html
    ├── css/site.css
    └── js/app.js
```

Run that assembled application with:

```bash
dotnet artifacts/publish/TradingRisk.Api.dll
```

### Container

The existing multi-stage `Dockerfile` runs `dotnet publish`. Therefore the final
ASP.NET runtime image receives the same `wwwroot` output; no Node image or
frontend build stage is required.

This is analogous to a Spring Boot artifact containing copied static resources,
although .NET publish output is normally a directory rather than one executable
JAR.

## 16. Automated testing

`RootServesBrowserWorkbenchAndItsStaticAssets` uses:

```csharp
new WebApplicationFactory<Program>()
```

This starts the actual `Program.cs` inside ASP.NET Core's in-process test host.
The test requests:

```text
/
/css/site.css
/js/app.js
```

and verifies successful status, content types, and identifying content.

This is stronger than `File.Exists("wwwroot/index.html")` because it exercises:

- discovery of the API content root;
- middleware order;
- default-file rewriting;
- static-file serving; and
- content-type mapping.

It does not execute the JavaScript or validate layout in a real browser. A
production UI test suite could add:

- JavaScript unit tests for request/read/render helpers;
- accessibility checks;
- browser component tests;
- a small Playwright end-to-end happy path;
- responsive visual-regression snapshots; and
- contract tests generated from OpenAPI.

Test at the cheapest boundary that proves the behavior. Domain formula tests
should remain in C# and should not be duplicated as browser tests.

### Spring comparison

`WebApplicationFactory<Program>` fills a role similar to a Spring Boot
integration test using `@SpringBootTest` plus `MockMvc` or a test web client:
start real application composition, send HTTP-shaped requests, and assert the
response without deploying an external server.

## 17. Debugging the complete flow

### Browser side

Use browser developer tools:

1. **Network**: inspect request URL, JSON body, status, and Problem Details;
2. **Console**: inspect JavaScript exceptions;
3. **Elements**: inspect actual DOM, computed CSS, hidden state, and labels;
4. **Accessibility**: inspect names, roles, and live regions; and
5. **Responsive mode**: try narrow layouts and table scrolling.

### ASP.NET Core side

Use Rider:

1. start `TradingRisk.Api` in Debug;
2. set a breakpoint in `PortfoliosController`;
3. submit the browser form;
4. step into the application handler;
5. step into `HistoricalSimulationRiskCalculator`; and
6. inspect the returned DTO before JSON serialization.

### Common failures

| Symptom | Inspect |
|---|---|
| `/` returns 404 | `UseDefaultFiles`/`UseStaticFiles`, order, and published `wwwroot/index.html` |
| page loads without styling | Network response for `/css/site.css` and its content type |
| buttons do nothing | console parse/runtime error and `/js/app.js` response |
| API status says unavailable | `/health`, HTTPS redirect/base URL, reverse proxy |
| API returns 400 | Network response Problem Details and request JSON field names/types |
| risk endpoint returns 404 | portfolio ID, active database connection, and whether that row exists |
| scenario validation fails | missing instrument return, duplicate date, return bounds, or confidence |
| date appears one day early | UTC handling when converting date-only values |
| page works locally but not after publish | confirm `wwwroot` is in publish/container output |

## 18. Choosing a .NET UI approach later

The current UI is one good point on a spectrum.

| Approach | Where rendering happens | Strengths | Costs / when it becomes awkward |
|---|---|---|---|
| Static HTML/CSS/JS + API (current) | browser | minimal toolchain, transparent HTTP, easy same-origin packaging | manual DOM/state organization grows expensive |
| Razor Pages | server | page-focused forms, model binding, validation, simple server rendering | full-page/request model; rich client interaction still needs JS |
| ASP.NET Core MVC views | server | controller/view separation, mature server-rendered pattern | more ceremony for a purely API-driven workbench |
| Blazor Server | server-side component state over a live connection | C# component model, thin browser download | connection/state scaling and latency considerations |
| Blazor WebAssembly | browser via WebAssembly | shared C# skills/types, rich component UI | larger download and a separate client runtime/model |
| React/Vue/Angular + TypeScript | browser, separate frontend build | mature ecosystems and large-app state/component tooling | Node toolchain, packages, bundling, CORS/proxy or integrated publish |

### Spring analogies

| .NET choice | Rough Java ecosystem analogy |
|---|---|
| Razor Pages / MVC views | Spring MVC + Thymeleaf |
| static API workbench | static JS client served by Spring Boot |
| Blazor components | server/WebAssembly component model with no exact Spring equivalent |
| React/Angular SPA + ASP.NET API | React/Angular SPA + Spring REST API |

Do not choose only by analogy. Team experience, accessibility, latency,
deployment boundaries, state complexity, component reuse, security model, and
operational ownership matter more.

### A sensible evolution path

Keep the current implementation while learning the complete request. Consider
TypeScript and a component framework when:

- multiple screens share substantial client state;
- DOM construction and event cleanup become hard to reason about;
- reusable interactive components dominate the application;
- client-side routing is required;
- several developers need compiler-checked API client types; or
- the UI becomes a separately owned/deployed product.

If that happens, preserve the same architectural rule: generated/API DTOs at
the outer edge, explicit state/use cases inside the client, and no copied
finance formulas.

## 19. Production hardening checklist

Before calling this a production trading-risk UI, address:

- authentication and authorization for every API operation;
- server-side user/book entitlements;
- HTTPS and secure response headers;
- a reviewed Content Security Policy;
- CSRF protection if cookie-based authentication is introduced;
- API and UI dependency inventory/scanning;
- idempotency and duplicate-submit behavior;
- persistent storage and optimistic concurrency;
- pagination/streaming for large scenario sets;
- market-data provenance and as-of timestamps;
- model version and methodology disclosure;
- audit records for inputs, outputs, actor, and calculation version;
- observability across browser, API, handlers, storage, and calculator;
- accessibility testing against the team's required standard;
- supported-browser policy;
- localization and exact decimal transport policy; and
- a clear disclaimer and approval workflow appropriate to actual use.

The current “educational implementation only” message is a boundary, not
production governance.

## 20. Exercises

### Exercise A — short exposure

Change one position quantity to a negative value. Before calculating, predict:

- net market value;
- gross exposure;
- the sign of its P&L when the instrument rises; and
- whether the position chip will be long or short.

Trace the answer from browser JSON through `Position.Create`.

### Exercise B — inspect the contract

Open the browser Network panel. Submit the sample and compare:

- request JSON with `CreatePortfolioRequest`;
- response JSON with `PortfolioDto`;
- risk request JSON with `CalculateRiskRequest`; and
- risk response JSON with `RiskReportDto`.

Explain why JSON uses camelCase while C# uses PascalCase.

### Exercise C — server validation wins

Use developer tools or the `.http` file to bypass an HTML restriction, such as
an invalid confidence. Follow the failure through Data Annotations,
Application/Domain validation, Problem Details, and `showError`.

### Exercise D — request cancellation

Add an `AbortController`, a visible Cancel button, and a deadline. Explain the
difference between aborting browser interest and rolling back already-completed
server work.

### Exercise E — exact generated client

Generate a TypeScript client from the Development OpenAPI document in an
experiment branch. Compare generated contract types with handwritten object
literals. Record the toolchain and versioning cost before deciding whether to
keep it.

### Exercise F — add a stress scenario

Add a separate named stress-scenario form and endpoint. Do not mix a hand-picked
stress loss into historical VaR observations. Explain the different semantics
in the UI and Domain.

### Exercise G — make the integration test fail

Swap `UseDefaultFiles` and `UseStaticFiles`, run the tests, and explain the
result. Restore the correct order afterward.

### Exercise H — migrate deliberately

Rebuild only the portfolio form as Razor Pages, Blazor, or TypeScript in a
temporary branch. Compare:

- dependencies;
- startup/build commands;
- validation duplication;
- generated output;
- testing strategy; and
- deployment artifact.

The objective is to understand the trade, not to maximize framework count.

## 21. Review questions

You should be able to answer:

1. Why does `UseDefaultFiles` not serve bytes by itself?
2. Why must it appear before `UseStaticFiles`?
3. Why are files under `wwwroot` different from `appsettings.json`?
4. Why does the Web SDK publish these assets without explicit `Content` items?
5. Why does same-origin hosting avoid a normal CORS configuration?
6. Why is client-side validation never sufficient?
7. Why does the UI render risk results rather than calculate them?
8. Why does `const` not make a JavaScript object immutable?
9. Why must code check `response.ok` after `fetch` resolves?
10. How does `CreatePortfolioRequest.Name` become JSON `name`?
11. Why is `textContent` safer for untrusted display values than `innerHTML`?
12. What precision difference exists between JavaScript `Number` and C#
    `decimal`?
13. Why is a date-only scenario formatted differently from a UTC timestamp?
14. What does the new `WebApplicationFactory` test prove, and what does it not?
15. When would Razor, Blazor, or a TypeScript SPA be a better choice?

If you can trace one click from the DOM, through JSON and controller binding,
into the handler and domain calculator, and back into accessible output, you
understand the UI as part of the complete .NET system rather than as an
unrelated frontend.
