# Deep dive: risk metrics and finance foundations

This chapter explains both the finance ideas and their exact implementation in
`HistoricalSimulationRiskCalculator`. It is an engineering introduction, not
model approval or trading advice.

## How to read the formulas

GitHub Markdown does not reliably render LaTeX math delimiters such as `\(...\)`
and `\[...\]`. The formulas in this chapter therefore use plain text inside
`text` code blocks. They are intentionally written like a calculator or a
spreadsheet so that the meaning stays visible in GitHub, Rider, and a terminal.

The most common symbols are:

| Symbol | Meaning | Beginner translation |
|---|---|---|
| `i` | position/instrument number | “which holding?” |
| `t` | scenario or date number | “which market day?” |
| `q` | signed quantity | shares/contracts; negative means short |
| `p` | current price | price per unit |
| `V` | market value/exposure | quantity × price |
| `r` | simple return | percentage move written as a decimal |
| `P&L` | profit and loss | money gained or lost |
| `L` | loss | negative P&L, so a bigger positive number is worse |
| `N` | number of observations | how many scenarios are in the sample |
| `c` | confidence level | usually `0.95` or `0.99`, not `95` or `99` |

Percentages must be converted to decimals before entering the API: 4% is
`0.04`, -4% is `-0.04`, and 100% is `1.00`.

## Finance in five minutes

Imagine buying one share for USD 100. If its price becomes USD 103, the simple
return is 3% (`103 / 100 - 1 = 0.03`). If you own 10 shares, your market value
is USD 1,000 and the approximate one-period P&L is USD 30 (`1,000 × 0.03`).

Risk measurement asks a different question from performance measurement:

- **Performance:** “How much did I make or lose in this period?”
- **Risk:** “How large could a loss be, under a stated model and sample?”

A portfolio is a collection of positions. A position is an exposure to an
instrument. A return is a percentage change in an instrument or factor. The
engine combines those three things into money P&L, then summarizes many P&Ls.

The word “risk” never has meaning by itself. Always attach the missing labels:

```text
USD 200 one-day 80% historical VaR
```

This means: using the supplied one-day historical sample, the 80th-percentile
loss is USD 200. It does not mean a guaranteed maximum loss, and it says
nothing about a ten-day horizon, another currency, or a different portfolio.

### Why losses are sorted

Suppose five scenario losses are `-50, -20, 0, 50, 100`. Negative losses are
profits, zero is break-even, and positive values are losses. Sorting them gives
the whole empirical distribution from best to worst:

```text
best/profit                              worst/loss
      -50       -20        0        50        100
       |         |         |         |          |
       +---------+---------+---------+----------+
```

VaR chooses a location in this ordered list. Expected Shortfall averages the
values at the bad end. Neither metric predicts a specific next-day result.

## What this implementation models

The portfolio contains linear positions in one base currency. For position `i`:

```text
market value V[i] = signed quantity q[i] × current price p[i]
```

Here `q[i]` is signed quantity and `p[i]` is current price. A short position
has a negative quantity.

For historical scenario `t`, the engine applies observed return `r[i,t]` to
today's position value:

```text
P&L[t] = sum over every position i of (V[i] × r[i,t])
```

and defines loss as:

```text
loss L[t] = - P&L[t]
```

A positive loss is bad; a negative loss is a profit. In plain language:

```text
gross exposure = add the absolute value of every position value
net market value = add the signed value of every position
```

## Position, direction, and market value

A position contains:

```text
instrument ID + signed quantity + current price
```

Quantity carries the direction:

| Position | Quantity | Market value | If price rises |
|---|---:|---:|---|
| long 100 shares at USD 20 | `+100` | `+2,000` | profit |
| short 100 shares at USD 20 | `-100` | `-2,000` | loss |

The code preserves that sign:

```csharp
public decimal MarketValue => Quantity * Price;
```

There is no separate `Long`/`Short` boolean that could contradict quantity.

For a one-period return `r`:

```text
P&L = exposure V × return r
```

| Exposure | Return | P&L | Interpretation |
|---:|---:|---:|---|
| +2,000 | +5% | +100 | long gains |
| +2,000 | -5% | -100 | long loses |
| -2,000 | +5% | -100 | short loses |
| -2,000 | -5% | +100 | short gains |

The calculator converts P&L to loss using `loss = - P&L`. Larger positive loss
values are worse, which makes quantile and tail calculations easier to explain.

### Simplifying instrument assumptions

`Position.Create` rejects negative price. That is appropriate for this
equity-like teaching model, not a universal market rule. Some rates and market
prices can be negative, and derivative values depend on valuation perspective.
A production model should define valid ranges per product.

Zero quantity is rejected because it has no current exposure. A trade/position
system may retain flat rows for history; a risk snapshot can model or filter
them explicitly.

## Net market value and gross exposure

Consider:

```text
Long  10 AAPL at USD 200 = +2,000
Short  5 MSFT at USD 400 = -2,000
```

Net market value is zero:

```text
net market value = +2,000 + (-2,000) = 0
```

Gross exposure is:

```text
gross exposure = absolute(+2,000) + absolute(-2,000) = 4,000
```

A zero net value does not mean zero risk because the instruments can move
differently. Gross exposure shows absolute position size, but ignores
volatility, correlation, liquidity, basis risk, and optionality.

A useful sanity property is:

```text
GrossExposure >= absolute(NetMarketValue)
```

This follows from the triangle inequality and is a good property-based test.

## Historical scenarios

One `HistoricalScenario` is a date and a coherent map of instrument returns:

```text
2026-01-02
  AAPL -> -4%
  MSFT -> -2%
```

“Coherent” matters. Moves from the same historical date retain that date's
cross-asset relationship. Combining each factor's worst unrelated date creates
a stress scenario, not an observed historical scenario.

The engine applies every historical vector to **today's** holdings. It does not
reconstruct the positions held on each old date.

`ReturnFor` fails when a scenario lacks a portfolio instrument. Treating missing
data as zero would silently assert that the instrument did not move.

A production policy must govern:

- new instruments with short history;
- different exchange holidays;
- stale, suspended, or missing prices;
- corporate actions and delistings;
- FX conversion;
- identifier changes;
- outliers and bad ticks.

Every proxy, repair, exclusion, or carry-forward changes the model and should
be recorded in data lineage.

## Simple and log returns

The engine accepts simple returns:

```text
simple return = (new price / old price) - 1
```

A conventional asset simple return cannot be below -100%, so the scenario
factory rejects a value below `-1m`.

Log return is:

```text
log return = natural log(new price / old price)
```

Log returns add across time, whereas simple returns compound:

```text
total growth factor = (1 + return[1]) × (1 + return[2]) × ...
```

Do not feed log returns into a formula expecting simple returns without an
explicit conversion. Small daily moves may look similar enough to hide the
error. For equities, use a governed adjusted-price policy so splits and
distributions do not appear as unexplained shocks.

## Historical Value at Risk

VaR at confidence `c` is an empirical loss quantile. This project sorts losses
from smallest to largest and uses the explicitly documented nearest-rank rule:

```text
one-based rank = ceiling(confidence c × number of observations N)
```

The loss at that one-based rank is VaR, floored at zero. Quantile conventions
differ between systems, so methodology is part of the contract, not an
implementation detail.

Interpretation: under the model and sample, a one-period loss should not exceed
the VaR threshold in approximately `c` of periods. It does **not** say what
the maximum loss is, nor that the probability statement will hold in the next
period.

## VaR step by step

Given scenario P&Ls:

```text
-100, -50, 0, +20, +50
```

convert to losses and sort ascending:

```text
-50, -20, 0, 50, 100
```

At `c = 0.80` and `N = 5`:

```text
rank = ceiling(0.80 × 5) = 4
```

The fourth loss is 50, so VaR is 50.

The implementation converts that one-based rank into a zero-based array index:

```csharp
var valueAtRiskRank = Math.Max(
    1,
    (int)Math.Ceiling(confidenceLevel * orderedLosses.Length));

var valueAtRisk = Math.Max(
    0m,
    orderedLosses[valueAtRiskRank - 1]);
```

`Math.Max(1, ...)` protects the index for small samples. `Math.Max(0m, ...)`
applies this report's convention: when all supplied scenarios are profitable,
reported VaR is zero rather than a negative loss.

### Quantile conventions differ

Statistical libraries offer nearest-rank, interpolated, lower, higher, midpoint,
and weighted quantiles. With short tails, the convention can materially change
the answer. A risk report must version the rule, not merely say “99% VaR.”

### Confidence and horizon

“99% VaR” is incomplete. State:

- one-day, ten-day, or other horizon;
- confidence level;
- sample window and weighting;
- quantile convention;
- P&L methodology;
- currency and valuation time.

One-day VaR is not automatically converted to ten-day VaR by multiplying by
`sqrt(10)`. That scaling assumes behavior that may fail under fat tails,
changing liquidity, serial dependence, and nonlinear products.

## Expected Shortfall

This project takes:

```text
tail count k = maximum(1, ceiling((1 - confidence c) × N))
```

and averages the worst `k` losses, again floored at zero. Expected Shortfall
describes the average severity in the modeled tail and normally should be at
least VaR for this empirical convention.

The Basel market-risk framework uses Expected Shortfall at a 97.5% one-tailed
confidence level in its internal-models approach. That regulatory fact does not
make this simple implementation regulatory-grade; Basel calculations add
liquidity horizons, risk-factor eligibility, stress calibration, backtesting,
and many governance requirements. See the
[Basel Committee's FRTB framework](https://www.bis.org/publ/bcbs265.pdf).

## Expected Shortfall step by step

For `c = 0.80` and `N = 5`:

```text
k = maximum(1, ceiling((1 - 0.80) × 5)) = 1
```

The worst one loss is 100, so ES is 100.

The code is:

```csharp
var tailObservationCount = Math.Max(
    1,
    (int)Math.Ceiling((1m - confidenceLevel) * orderedLosses.Length));

var expectedShortfall = Math.Max(
    0m,
    orderedLosses.TakeLast(tailObservationCount).Average());
```

At lower confidence or with more observations, ES averages several tail
losses. Under this project's empirical convention, ES should be at least VaR.

VaR gives a threshold but does not describe what happens beyond it:

```text
Portfolio A tail: 101, 102, 103
Portfolio B tail: 101, 500, 2,000
```

Both can have the same VaR threshold and radically different Expected
Shortfall. ES is still statistically noisy when only a few observations occupy
the tail. More observations are not automatically representative if the market
regime has changed.

## Worst loss

The code selects:

```csharp
Math.Max(0m, orderedLosses[^1])
```

`[^1]` means the final array element. Because the array is sorted ascending,
that is the worst observed loss.

Worst historical loss is not a theoretical maximum loss. It is only the largest
loss in the supplied sample. Stress testing should include severe plausible or
hypothetical events that may not exist in the window.

## Volatility

The daily P&L volatility is the sample standard deviation:

```text
variance = sum((P&L[t] - average P&L)^2) / (N - 1)
volatility s = square root(variance)
```

The example annualizes it using:

```text
annualized volatility = daily volatility s × square root(252)
```

This assumes independent, identically distributed daily changes and roughly 252
trading days. That square-root-of-time scaling can be misleading under
autocorrelation, volatility clustering, changing positions, or long horizons.

## Sample standard deviation in the code

The calculator converts decimal P&Ls to `double`:

```csharp
var values = observations.Select(decimal.ToDouble).ToArray();
```

Then:

```csharp
var mean = values.Average();
var squaredDeviations = values.Sum(value => Math.Pow(value - mean, 2d));
var variance = squaredDeviations / (values.Length - 1);
return (decimal)Math.Sqrt(variance);
```

The denominator is `N - 1` because the mean was estimated from the same sample.
With one observation, the implementation returns zero because sample variance
cannot be estimated with a zero denominator. Do not interpret that as evidence
of zero risk.

`decimal` is useful for base-10 prices, quantities, and monetary reporting.
`Math.Sqrt` operates on `double`, so the implementation converts for this
statistical operation. Production numerical code must define accuracy,
rounding, overflow, scale, and independently verified tolerances. “Use decimal
everywhere” is not a substitute for a numerical error policy.

Volatility is dispersion around the mean and treats upside/downside deviations
symmetrically. VaR and ES summarize a modeled loss tail. They answer different
questions.

## Worked example used by the test

One position has quantity 100 and price USD 10, so its value is USD 1,000.
Historical returns are:

```text
-10%, -5%, 0%, +2%, +5%
```

P&Ls are `-100, -50, 0, +20, +50`; losses sorted ascending are:

```text
-50, -20, 0, 50, 100
```

At 80% confidence, the nearest rank is `ceil(0.8 * 5) = 4`, so VaR is USD 50.
The worst `ceil(0.2 * 5) = 1` observation is USD 100, so ES is USD 100. The test
in `HistoricalSimulationRiskCalculatorTests` locks down that convention.

## Worked two-position API example

The API integration test creates:

```text
AAPL: quantity 100 × price 200 = USD 20,000
MSFT: quantity  50 × price 400 = USD 20,000
Total net/gross value             = USD 40,000
```

It applies:

| Date | AAPL | MSFT | P&L | Loss |
|---|---:|---:|---:|---:|
| 2026-01-02 | -4% | -2% | -1,200 | 1,200 |
| 2026-01-05 | -2% | +1% | -200 | 200 |
| 2026-01-06 | 0% | 0% | 0 | 0 |
| 2026-01-07 | +1% | +2% | 600 | -600 |
| 2026-01-08 | +3% | +2% | 1,000 | -1,000 |

Sorted losses:

```text
-1,000, -600, 0, 200, 1,200
```

At 80%:

```text
rank = ceil(0.8 × 5) = 4       -> VaR = 200
tail = ceil(0.2 × 5) = 1       -> ES = 1,200
```

That is why the HTTP test asserts:

```csharp
Assert.Equal(200m, report.ValueAtRisk);
Assert.Equal(1_200m, report.ExpectedShortfall);
```

The broad test proves the whole HTTP-to-domain slice. The focused domain test
is still the clearer specification of the quantile convention.

## Calculation pipeline and complexity

The engine performs:

```text
validated portfolio + historical scenarios
  -> order scenarios by date
  -> revalue each position under each scenario
  -> create P&L/loss observations
  -> sort losses
  -> select VaR and worst loss
  -> average the ES tail
  -> compute and annualize P&L volatility
  -> return RiskReport
```

For `S` scenarios and `P` positions:

```text
revaluation: O(S × P)
loss sorting: O(S log S)
working memory: approximately O(S), excluding inputs
```

For large inputs, return lookup and multiplication can dominate sorting.
Measure realistic portfolio/scenario shapes before parallelizing or changing
data structures.

## Linear approximation and nonlinear instruments

The current model assumes:

```text
change in value ΔV[i] ≈ current value V[i] × return r[i]
```

This is transparent for simple spot-like positions. It is insufficient for:

- options with spot curvature, volatility, and time dependence;
- bonds with curves, coupons, duration, convexity, and spread;
- path-dependent or barrier products;
- cash flows and corporate actions;
- multiple currencies without FX shocks;
- basis relationships between instruments and proxy factors.

For an option, a local approximation might include several sensitivities:

```text
change in option value ≈
    delta × change in underlying price
  + 0.5 × gamma × (change in underlying price)^2
  + vega × change in implied volatility
  + theta × change in time
```

Full revaluation reprices each instrument under every scenario. It is usually
more faithful and more computationally expensive, and it requires complete
market inputs plus validated pricing models.

## Comparing risk approaches

### Historical simulation

Replays historical factor moves on today's portfolio.

Strengths:

- explainable;
- retains observed same-date cross-factor relationships;
- no explicit normal-distribution assumption.

Weaknesses:

- limited by the chosen history;
- sparse tail;
- sensitive to window and data treatment;
- assumes history is relevant to the current regime.

### Parametric or delta-normal VaR

Uses exposure and a covariance model:

```text
portfolio volatility = square root(weights transpose × covariance matrix × weights)
parametric VaR = normal-distribution z-score × portfolio volatility
```

It is fast and decomposable, but linear/normal assumptions miss skew, fat
tails, and nonlinear products. Factor mapping and covariance estimation are
model choices.

### Monte Carlo

Simulates many factor paths from a stochastic model and revalues the portfolio.
It supports richer products and scenarios, but introduces calibration/model
risk, sampling error, compute cost, and reproducibility requirements for seeds,
algorithms, model versions, and inputs.

### Stress testing

Applies severe historical or hypothetical scenarios without claiming a
reliable occurrence probability.

Stress asks “what happens if this event occurs?” VaR asks a quantile question
under a statistical/sample model. A mature framework uses both.

## Backtesting and P&L definitions

Backtesting compares a prior forecast to later P&L. “P&L” must be defined:

- actual P&L includes trades, fees, intraday changes, and other effects;
- hypothetical P&L holds prior positions and applies subsequent market moves;
- clean P&L removes selected effects under a governed definition.

Prevent look-ahead bias:

```text
forecast at end of day t uses only information available at t
compare with the governed P&L for t -> t+1
```

Store the original forecast and input versions. Recalculating it later with
today's code or corrected data is not the same backtest.

## Confidence is not certainty

A 99% one-day VaR does not mean:

- the loss can exceed it only once per exactly 100 days;
- it is the maximum possible loss;
- the model is “99% accurate”;
- observations are independent;
- the quantile is precisely estimated;
- the result remains valid after positions or regime change.

It is a conditional estimate with model and sampling uncertainty.

## Data lineage and time

Distinguish:

- position effective time;
- market-data effective and received time;
- valuation time;
- calculation start/completion;
- business date, calendar, and time zone.

`DateOnly` is useful for a daily scenario label but cannot represent all those
concepts.

Trace:

```text
source -> raw observation -> quality adjustment -> return -> scenario set
       -> portfolio snapshot + methodology -> result
```

If data is corrected, retain which calculations used the original value and
define whether reports are restated.

## Important limitations

Do not use the current engine for trading, capital, or limit decisions:

- Linear shock approximation: no option curvature, path dependence, cash flows,
  coupons, dividends, or full repricing.
- One currency: there is no FX conversion or basis risk.
- Current holdings: it replays returns on today's portfolio and does not model
  historical changes in holdings.
- Tiny samples in examples: production historical simulation typically needs a
  much larger governed window. RiskMetrics' practical guide discusses using at
  least roughly a year of recent daily market rates for historical simulation:
  [Risk Management: A Practical Guide](https://www.msci.com/resources/research/technical_documentation/RMGuide.pdf).
- Data quality: missing returns fail fast, but stale prices, corporate actions,
  calendars, outliers, and synchronized timestamps are not handled.
- Statistical uncertainty: VaR/ES estimates are noisy, especially in the tail.
- No backtesting: forecasts are not compared with realized clean/hypothetical
  P&L.
- No model governance: there is no independent validation, approval, versioned
  methodology, lineage, or audit workflow.

The classic
[RiskMetrics Technical Document](https://www.msci.com/documents/10199/5915b101-4206-4ba0-aee2-3449d5c7e95a)
is useful historical reading for volatility/correlation estimation and VaR.

## Finance vocabulary to learn next

- Return: `(new price / old price) - 1`; log return is the natural log of
  `(new price / old price)`.
- Mark-to-market: valuing a position with current market inputs.
- P&L explain: attributing value change to market moves, trades, carry, fees,
  and unexplained residual.
- Risk factor: a market input whose move changes value, such as equity spot,
  yield-curve tenor, FX spot, or implied volatility.
- Scenario: a coherent set of risk-factor shocks.
- Stress test: loss under an extreme historical or hypothetical scenario,
  usually not assigned a reliable probability.
- Backtesting: comparing forecast risk with realized or hypothetical P&L.
- Delta/gamma/vega: first/second sensitivity to underlying price and sensitivity
  to volatility.
- PV01/DV01: value change for a one-basis-point interest-rate move, subject to
  desk convention.
- Component VaR: additive allocation of portfolio VaR to positions/factors
  under a chosen model.
- Incremental VaR: difference in VaR with and without a proposed position.
- Marginal VaR: local sensitivity of portfolio VaR to a small exposure change.

## Risk factor versus instrument

An instrument is held; a risk factor is a market variable that changes its
value. One instrument can depend on many factors:

```text
EUR-denominated equity option
  -> equity spot
  -> implied-volatility surface
  -> interest-rate curves
  -> dividend assumptions
  -> EUR/base-currency FX
  -> time and calendar
```

The learning model uses `InstrumentId` as both position identifier and return
key. That shortcut should be replaced when richer products are added.

## Correlation and diversification

Correlation describes standardized co-movement, not causation or an unchanging
law. Historical scenarios preserve observed same-date co-movement. Parametric
models estimate covariance/correlation explicitly.

Positions that do not move perfectly together may diversify risk. Correlations
can behave differently in crises, so an ordinary-period estimate can overstate
the protection. Gross exposure ignores diversification; VaR/ES can recognize it
only to the extent that the scenario/model captures it.

## Sensitivities and units

- delta: first-order value sensitivity to an underlying factor;
- gamma: second-order curvature/change in delta;
- vega: value sensitivity to implied volatility;
- theta: value sensitivity to passage of time;
- PV01/DV01: value response to a one-basis-point rate move under a convention.

Always state factor, bump size, units, direction, and whether other inputs are
held fixed or recalibrated.

## Liquidity, market, and counterparty risk

Statistical price movement is not the complete cost of exiting a position.
Bid/ask spread, depth, concentration, liquidation horizon, and market impact
matter. A one-day metric can be misleading for an asset that cannot be exited
in one day.

Market risk measures value changes from market factors. Counterparty credit
risk concerns loss if the counterparty defaults while exposure is positive,
including netting, collateral, future exposure, and wrong-way risk. They may
share trades and market data but require different models.

## Model and software controls

Every methodology should have:

- stable model/methodology ID and version;
- owner and independent validator;
- approved use and product scope;
- formulas and parameters;
- data sources and quality rules;
- golden/benchmark data;
- backtesting and monitoring;
- known limitations;
- change approval/effective date;
- reproducible implementation version.

Software tests show that code conforms to a specification. Model validation
asks whether the specification is appropriate.

## Questions every risk report must answer

- What is the valuation time and market-data snapshot?
- Which positions and trades are included?
- What base currency and FX conversion rules are used?
- What horizon, confidence, sample window, weighting, and quantile convention?
- Is P&L full revaluation or an approximation?
- How are missing, stale, and extreme observations handled?
- Which model/code version produced the result?
- Can the exact inputs and output be reproduced?

## Questions to ask a risk expert on the new team

- Which P&L sign convention and report currency are authoritative?
- Does “position” mean trade, lot, settled holding, or net exposure?
- Which cut-off, time zone, calendar, and business date apply?
- How are prices adjusted and returns constructed?
- How are missing, stale, and extreme observations handled?
- What sample window, weighting, horizon, and confidence are used?
- Which quantile and ES conventions are implemented?
- Is valuation full revaluation, sensitivities, or a linear proxy?
- How are options, FX, rates, correlation, and basis risks modeled?
- Which P&L definition is used for backtesting?
- Which stress scenarios and limits are mandatory?
- How are model versions approved and reports restated?
- What lineage must be retained for audit?
