const samplePortfolio = {
  name: "Learning Book",
  baseCurrency: "USD",
  positions: [
    { instrumentId: "AAPL", quantity: 100, price: 200 },
    { instrumentId: "MSFT", quantity: 50, price: 400 }
  ]
};

const sampleDates = [
  "2026-01-02",
  "2026-01-05",
  "2026-01-06",
  "2026-01-07",
  "2026-01-08"
];

const sampleReturnColumns = [
  [-0.04, -0.02, 0, 0.01, 0.03],
  [-0.02, 0.01, 0, 0.02, 0.02],
  [-0.03, -0.01, 0, 0.015, 0.025]
];

const state = {
  portfolio: null,
  positionRowCounter: 0,
  scenarioRowCounter: 0,
  dashboardPage: 1,
  dashboardTotalPages: 0,
  accessToken: null
};

const elements = {
  apiState: document.querySelector(".api-state"),
  apiStatusText: document.querySelector("#api-status-text"),
  loadSampleButton: document.querySelector("#load-sample-button"),
  portfolioForm: document.querySelector("#portfolio-form"),
  portfolioName: document.querySelector("#portfolio-name"),
  baseCurrency: document.querySelector("#base-currency"),
  positionList: document.querySelector("#position-list"),
  addPositionButton: document.querySelector("#add-position-button"),
  createPortfolioButton: document.querySelector("#create-portfolio-button"),
  portfolioPanelState: document.querySelector("#portfolio-panel-state"),
  portfolioSummary: document.querySelector("#portfolio-summary"),
  summaryName: document.querySelector("#summary-name"),
  summaryNetValue: document.querySelector("#summary-net-value"),
  summaryGrossExposure: document.querySelector("#summary-gross-exposure"),
  summaryId: document.querySelector("#summary-id"),
  positionChips: document.querySelector("#position-chips"),
  editPortfolioButton: document.querySelector("#edit-portfolio-button"),
  scenarioPanelState: document.querySelector("#scenario-panel-state"),
  riskLockedState: document.querySelector("#risk-locked-state"),
  riskForm: document.querySelector("#risk-form"),
  confidenceLevel: document.querySelector("#confidence-level"),
  addScenarioButton: document.querySelector("#add-scenario-button"),
  scenarioTableHead: document.querySelector("#scenario-table-head"),
  scenarioTableBody: document.querySelector("#scenario-table-body"),
  calculateRiskButton: document.querySelector("#calculate-risk-button"),
  queueRiskButton: document.querySelector("#queue-risk-button"),
  riskReport: document.querySelector("#risk-report"),
  reportSubtitle: document.querySelector("#report-subtitle"),
  reportTime: document.querySelector("#report-time"),
  metricVar: document.querySelector("#metric-var"),
  metricVarNote: document.querySelector("#metric-var-note"),
  metricEs: document.querySelector("#metric-es"),
  metricWorst: document.querySelector("#metric-worst"),
  metricVolatility: document.querySelector("#metric-volatility"),
  metricAnnualized: document.querySelector("#metric-annualized"),
  pnlChart: document.querySelector("#pnl-chart"),
  chartDescription: document.querySelector("#chart-description"),
  interpretationCopy: document.querySelector("#interpretation-copy"),
  resultCount: document.querySelector("#result-count"),
  resultTableBody: document.querySelector("#result-table-body"),
  errorAlert: document.querySelector("#error-alert"),
  errorTitle: document.querySelector("#error-title"),
  errorMessage: document.querySelector("#error-message"),
  errorDetails: document.querySelector("#error-details"),
  dismissErrorButton: document.querySelector("#dismiss-error-button"),
  toast: document.querySelector("#toast"),
  refreshDashboardButton: document.querySelector("#refresh-dashboard-button"),
  dashboardSearchButton: document.querySelector("#dashboard-search-button"),
  dashboardNameFilter: document.querySelector("#dashboard-name-filter"),
  dashboardCurrencyFilter: document.querySelector("#dashboard-currency-filter"),
  dashboardPageSize: document.querySelector("#dashboard-page-size"),
  dashboardPortfolioCount: document.querySelector("#dashboard-portfolio-count"),
  dashboardPositionCount: document.querySelector("#dashboard-position-count"),
  dashboardCurrencyCount: document.querySelector("#dashboard-currency-count"),
  dashboardTableBody: document.querySelector("#dashboard-table-body"),
  dashboardPageLabel: document.querySelector("#dashboard-page-label"),
  dashboardResultLabel: document.querySelector("#dashboard-result-label"),
  dashboardPreviousButton: document.querySelector("#dashboard-previous-button"),
  dashboardNextButton: document.querySelector("#dashboard-next-button"),
  currencyBreakdown: document.querySelector("#currency-breakdown"),
  viewLinks: document.querySelectorAll("[data-view-link]")
  ,loginForm: document.querySelector("#login-form")
  ,loginUser: document.querySelector("#login-user")
  ,loginRole: document.querySelector("#login-role")
  ,logoutButton: document.querySelector("#logout-button")
  ,authStatus: document.querySelector("#auth-status")
};

class ApiError extends Error {
  constructor(status, title, detail, validationErrors = []) {
    super(detail || title);
    this.status = status;
    this.title = title;
    this.validationErrors = validationErrors;
  }
}

function createElement(tagName, className, text) {
  const element = document.createElement(tagName);

  if (className) {
    element.className = className;
  }

  if (text !== undefined) {
    // textContent is deliberate: API/user values must never become executable HTML.
    element.textContent = text;
  }

  return element;
}

function addPositionRow(position = { instrumentId: "", quantity: 0, price: 0 }) {
  state.positionRowCounter += 1;
  const rowNumber = state.positionRowCounter;
  const row = createElement("div", "position-row");
  row.dataset.positionRow = String(rowNumber);

  const fields = [
    {
      label: "Instrument",
      name: "instrumentId",
      type: "text",
      value: position.instrumentId,
      attributes: { maxlength: "32", autocapitalize: "characters" }
    },
    {
      label: "Quantity",
      name: "quantity",
      type: "number",
      value: position.quantity,
      attributes: { step: "any" }
    },
    {
      label: "Price",
      name: "price",
      type: "number",
      value: position.price,
      attributes: { min: "0", step: "any" }
    }
  ];

  for (const field of fields) {
    const label = createElement("label");
    const labelText = createElement("span", "mobile-label", field.label);
    const input = document.createElement("input");
    const inputId = `position-${rowNumber}-${field.name}`;

    input.id = inputId;
    input.name = field.name;
    input.type = field.type;
    input.value = String(field.value);
    input.required = true;
    input.setAttribute("aria-label", field.label);

    for (const [attribute, value] of Object.entries(field.attributes)) {
      input.setAttribute(attribute, value);
    }

    label.htmlFor = inputId;
    label.append(labelText, input);
    row.append(label);
  }

  const removeButton = createElement("button", "remove-row-button", "×");
  removeButton.type = "button";
  removeButton.setAttribute("aria-label", `Remove position ${rowNumber}`);
  removeButton.addEventListener("click", () => {
    row.remove();
    updatePositionRemoveButtons();
  });
  row.append(removeButton);

  elements.positionList.append(row);
  updatePositionRemoveButtons();
}

function updatePositionRemoveButtons() {
  const rows = elements.positionList.querySelectorAll(".position-row");

  for (const button of elements.positionList.querySelectorAll(".remove-row-button")) {
    button.disabled = rows.length === 1;
  }
}

function readPortfolioForm() {
  const positions = [...elements.positionList.querySelectorAll(".position-row")].map(
    row => ({
      instrumentId: row.querySelector('[name="instrumentId"]').value.trim(),
      quantity: Number(row.querySelector('[name="quantity"]').value),
      price: Number(row.querySelector('[name="price"]').value)
    })
  );

  return {
    name: elements.portfolioName.value.trim(),
    baseCurrency: elements.baseCurrency.value.trim().toUpperCase(),
    positions
  };
}

function fillPortfolioForm(portfolio, shouldFocus = true) {
  elements.portfolioName.value = portfolio.name;
  elements.baseCurrency.value = portfolio.baseCurrency;
  elements.positionList.replaceChildren();

  for (const position of portfolio.positions) {
    addPositionRow(position);
  }

  elements.portfolioForm.hidden = false;
  elements.portfolioSummary.hidden = true;
  if (shouldFocus) {
    elements.portfolioName.focus();
  }
}

async function submitPortfolio(event) {
  event.preventDefault();
  hideError();
  setButtonBusy(elements.createPortfolioButton, true, "Creating");

  try {
    const portfolio = await sendJson(
      "/api/v1/portfolios",
      "POST",
      readPortfolioForm()
    );

    state.portfolio = portfolio;
    renderPortfolioSummary(portfolio);
    unlockRiskForm(portfolio);
    setJourneyState("scenarios");
    loadDashboard(state.dashboardPage);
    showToast("Portfolio created. The scenario matrix is ready.");
    document.querySelector(".scenarios-panel").scrollIntoView({
      behavior: prefersReducedMotion() ? "auto" : "smooth",
      block: "start"
    });
  } catch (error) {
    showError(error);
  } finally {
    setButtonBusy(elements.createPortfolioButton, false, "Create portfolio", "→");
  }
}

function renderPortfolioSummary(portfolio) {
  elements.portfolioForm.hidden = true;
  elements.portfolioSummary.hidden = false;
  elements.portfolioPanelState.textContent = "Active";
  elements.portfolioPanelState.classList.add("is-ready");
  elements.summaryName.textContent = portfolio.name;
  elements.summaryNetValue.textContent = formatMoney(
    portfolio.netMarketValue,
    portfolio.baseCurrency
  );
  elements.summaryGrossExposure.textContent = formatMoney(
    portfolio.grossExposure,
    portfolio.baseCurrency
  );
  elements.summaryId.textContent = portfolio.id;
  elements.positionChips.replaceChildren();

  for (const position of portfolio.positions) {
    const chip = createElement(
      "span",
      `position-chip${position.quantity < 0 ? " is-short" : ""}`
    );
    const marker = createElement("i");
    marker.setAttribute("aria-hidden", "true");
    chip.append(
      marker,
      document.createTextNode(
        `${position.instrumentId} · ${position.quantity < 0 ? "SHORT" : "LONG"}`
      )
    );
    elements.positionChips.append(chip);
  }
}

function unlockRiskForm(portfolio) {
  elements.riskLockedState.hidden = true;
  elements.riskForm.hidden = false;
  elements.scenarioPanelState.textContent = "Ready";
  elements.scenarioPanelState.classList.add("is-ready");
  elements.riskReport.hidden = true;
  renderScenarioMatrix(portfolio.positions);
}

function renderScenarioMatrix(positions) {
  elements.scenarioTableHead.replaceChildren();
  elements.scenarioTableBody.replaceChildren();

  const headerRow = document.createElement("tr");
  const dateHeader = createElement("th", null, "As-of date");
  dateHeader.scope = "col";
  headerRow.append(dateHeader);

  for (const position of positions) {
    const header = createElement("th", null, position.instrumentId);
    header.scope = "col";
    headerRow.append(header);
  }

  const actionHeader = document.createElement("th");
  actionHeader.scope = "col";
  actionHeader.append(createElement("span", "visually-hidden", "Actions"));
  headerRow.append(actionHeader);
  elements.scenarioTableHead.append(headerRow);

  sampleDates.forEach((date, scenarioIndex) => {
    addScenarioRow(date, positions, scenarioIndex);
  });
}

function addScenarioRow(
  date = nextScenarioDate(),
  positions = state.portfolio?.positions ?? [],
  scenarioIndex = elements.scenarioTableBody.children.length
) {
  state.scenarioRowCounter += 1;
  const rowNumber = state.scenarioRowCounter;
  const row = document.createElement("tr");
  row.dataset.scenarioRow = String(rowNumber);

  const dateCell = document.createElement("td");
  const dateInput = document.createElement("input");
  dateInput.type = "date";
  dateInput.name = "asOfDate";
  dateInput.value = date;
  dateInput.required = true;
  dateInput.setAttribute("aria-label", `Scenario ${rowNumber} date`);
  dateCell.append(dateInput);
  row.append(dateCell);

  positions.forEach((position, instrumentIndex) => {
    const returnCell = document.createElement("td");
    const returnInput = document.createElement("input");
    const returnColumn =
      sampleReturnColumns[instrumentIndex % sampleReturnColumns.length];

    returnInput.type = "number";
    returnInput.name = "return";
    returnInput.step = "any";
    returnInput.min = "-1";
    returnInput.value = String(returnColumn[scenarioIndex] ?? 0);
    returnInput.required = true;
    returnInput.dataset.instrumentId = position.instrumentId;
    returnInput.setAttribute(
      "aria-label",
      `${position.instrumentId} return for ${date}`
    );
    returnCell.append(returnInput);
    row.append(returnCell);
  });

  const actionCell = document.createElement("td");
  const removeButton = createElement("button", "remove-row-button", "×");
  removeButton.type = "button";
  removeButton.setAttribute("aria-label", `Remove scenario ${rowNumber}`);
  removeButton.addEventListener("click", () => {
    row.remove();
    updateScenarioRemoveButtons();
  });
  actionCell.append(removeButton);
  row.append(actionCell);

  elements.scenarioTableBody.append(row);
  updateScenarioRemoveButtons();
}

function nextScenarioDate() {
  const lastDateInput = elements.scenarioTableBody.querySelector(
    "tr:last-child input[type='date']"
  );
  const date = lastDateInput
    ? new Date(`${lastDateInput.value}T00:00:00Z`)
    : new Date("2026-01-02T00:00:00Z");

  date.setUTCDate(date.getUTCDate() + 1);
  return date.toISOString().slice(0, 10);
}

function updateScenarioRemoveButtons() {
  const rows = elements.scenarioTableBody.querySelectorAll("tr");

  for (const button of elements.scenarioTableBody.querySelectorAll(
    ".remove-row-button"
  )) {
    button.disabled = rows.length === 1;
  }
}

function readRiskForm() {
  const scenarios = [...elements.scenarioTableBody.querySelectorAll("tr")].map(
    row => {
      const returns = {};

      for (const input of row.querySelectorAll('input[name="return"]')) {
        returns[input.dataset.instrumentId] = Number(input.value);
      }

      return {
        asOfDate: row.querySelector('input[name="asOfDate"]').value,
        returns
      };
    }
  );

  return {
    confidenceLevel: Number(elements.confidenceLevel.value) / 100,
    scenarios
  };
}

async function submitRisk(event) {
  event.preventDefault();

  if (!state.portfolio) {
    return;
  }

  hideError();
  setButtonBusy(elements.calculateRiskButton, true, "Calculating");

  try {
    const report = await sendJson(
      `/api/v1/portfolios/${state.portfolio.id}/risk`,
      "POST",
      readRiskForm()
    );

    renderRiskReport(report);
    setJourneyState("report");
    showToast("Risk calculation completed.");
    elements.riskReport.scrollIntoView({
      behavior: prefersReducedMotion() ? "auto" : "smooth",
      block: "start"
    });
  } catch (error) {
    showError(error);
  } finally {
    setButtonBusy(elements.calculateRiskButton, false, "Calculate risk", "↗");
  }
}

async function submitQueuedRisk() {
  if (!state.portfolio) return;
  hideError();
  setButtonBusy(elements.queueRiskButton, true, "Queueing");
  try {
    const job = await sendJson("/api/v1/risk-jobs", "POST", {
      portfolioId: state.portfolio.id,
      ...readRiskForm()
    });
    showToast(`Job ${job.jobId} queued. Waiting for a worker…`);
    const result = await waitForRiskJob(job.jobId);
    renderRiskReport(result);
    setJourneyState("report");
    showToast("Queued risk calculation completed.");
  } catch (error) {
    showError(error);
  } finally {
    setButtonBusy(elements.queueRiskButton, false, "Queue calculation", "⇢");
  }
}

async function waitForRiskJob(jobId) {
  for (let attempt = 0; attempt < 100; attempt += 1) {
    const job = await getJson(`/api/v1/risk-jobs/${jobId}`);
    if (job.status === "succeeded") return job.result;
    if (job.status === "failed") throw new Error(job.error || "Queued risk job failed.");
    await new Promise(resolve => setTimeout(resolve, 300));
  }
  throw new Error("Queued risk job did not finish within 30 seconds.");
}

function renderRiskReport(report) {
  const confidencePercent = report.confidenceLevel * 100;
  const currency = report.currency;

  elements.riskReport.hidden = false;
  elements.reportSubtitle.textContent =
    `${report.scenarioCount} observations · ${formatPercent(confidencePercent)} confidence · ${currency}`;
  elements.reportTime.textContent = new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(new Date(report.calculatedAtUtc));
  elements.metricVar.textContent = formatMoney(report.valueAtRisk, currency);
  elements.metricVarNote.textContent =
    `${formatPercent(confidencePercent)} nearest-rank loss threshold`;
  elements.metricEs.textContent = formatMoney(report.expectedShortfall, currency);
  elements.metricWorst.textContent = formatMoney(report.worstLoss, currency);
  elements.metricVolatility.textContent = formatMoney(
    report.dailyPnlVolatility,
    currency
  );
  elements.metricAnnualized.textContent =
    `${formatMoney(report.annualizedPnlVolatility, currency)} annualized`;
  elements.interpretationCopy.textContent =
    `At ${formatPercent(confidencePercent)} confidence, the modeled one-period loss threshold is ` +
    `${formatMoney(report.valueAtRisk, currency)}. When that tail is reached, the average ` +
    `modeled loss is ${formatMoney(report.expectedShortfall, currency)}. This is a sample-based ` +
    `estimate, not a maximum-loss guarantee.`;

  renderChart(report.scenarioResults, currency);
  renderResultTable(report.scenarioResults, currency);
}

function renderChart(results, currency) {
  elements.pnlChart.replaceChildren();
  const maximumAbsolutePnl = Math.max(
    1,
    ...results.map(result => Math.abs(result.profitAndLoss))
  );

  for (const result of results) {
    const isProfit = result.profitAndLoss >= 0;
    const heightPercent = Math.max(
      2,
      Math.abs(result.profitAndLoss) / maximumAbsolutePnl * 43
    );
    const column = createElement("div", "chart-column");
    const bar = createElement(
      "span",
      `chart-bar ${isProfit ? "is-profit" : "is-loss"}`
    );
    const value = createElement(
      "span",
      `chart-value ${isProfit ? "is-profit" : "is-loss"}`,
      formatCompactNumber(result.profitAndLoss)
    );
    const date = createElement(
      "span",
      "chart-date",
      formatShortDate(result.asOfDate)
    );

    bar.style.height = `${heightPercent}%`;
    value.style.setProperty("--bar-height", `${heightPercent}%`);
    column.title =
      `${result.asOfDate}: ${formatMoney(result.profitAndLoss, currency)} P&L`;
    column.append(bar, value, date);
    elements.pnlChart.append(column);
  }

  elements.chartDescription.textContent = results
    .map(
      result =>
        `${result.asOfDate}: ${formatMoney(result.profitAndLoss, currency)}`
    )
    .join("; ");
}

function renderResultTable(results, currency) {
  elements.resultTableBody.replaceChildren();
  elements.resultCount.textContent = `${results.length} observations`;

  for (const result of results) {
    const row = document.createElement("tr");
    const outcome =
      result.profitAndLoss > 0
        ? { label: "Profit", className: "is-profit" }
        : result.profitAndLoss < 0
          ? { label: "Loss", className: "is-loss" }
          : { label: "Flat", className: "is-flat" };

    row.append(
      createTableCell(formatLongDate(result.asOfDate)),
      createTableCell(formatMoney(result.profitAndLoss, currency)),
      createTableCell(formatMoney(result.loss, currency))
    );

    const outcomeCell = document.createElement("td");
    outcomeCell.append(
      createElement(
        "span",
        `outcome ${outcome.className}`,
        outcome.label
      )
    );
    row.append(outcomeCell);
    elements.resultTableBody.append(row);
  }
}

function createTableCell(text) {
  return createElement("td", null, text);
}

function setView(view) {
  const activeView = view === "workbench" ? "workbench" : "overview";
  for (const section of document.querySelectorAll("[data-view]")) {
    section.hidden = section.dataset.view !== activeView;
  }
  for (const link of elements.viewLinks) {
    const isActive = link.dataset.viewLink === activeView;
    link.classList.toggle("is-active", isActive);
    link.setAttribute("aria-current", isActive ? "page" : "false");
  }
  if (activeView === "workbench") {
    document.querySelector("#workbench")?.scrollIntoView({
      behavior: prefersReducedMotion() ? "auto" : "smooth",
      block: "start"
    });
  }
}

function setViewFromHash() {
  setView(window.location.hash === "#workbench" ? "workbench" : "overview");
}

async function loadDashboard(page = state.dashboardPage) {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: elements.dashboardPageSize.value,
  });
  const name = elements.dashboardNameFilter.value.trim();
  const currency = elements.dashboardCurrencyFilter.value.trim().toUpperCase();

  if (name) params.set("name", name);
  if (currency) params.set("baseCurrency", currency);

  setButtonBusy(elements.refreshDashboardButton, true, "Refreshing", "↻");
  try {
    const [portfolioPage, statistics] = await Promise.all([
      getJson(`/api/v1/portfolios?${params.toString()}`),
      getJson("/api/v1/portfolios/statistics/by-currency")
    ]);

    state.dashboardPage = portfolioPage.page;
    state.dashboardTotalPages = portfolioPage.totalPages;
    elements.dashboardPortfolioCount.textContent = statistics.portfolioCount;
    elements.dashboardPositionCount.textContent = statistics.positionCount;
    elements.dashboardCurrencyCount.textContent = statistics.byCurrency.length;
    elements.dashboardPageLabel.textContent =
      `Page ${portfolioPage.page} of ${Math.max(1, portfolioPage.totalPages)}`;
    elements.dashboardResultLabel.textContent =
      `${portfolioPage.totalCount} persisted portfolio${portfolioPage.totalCount === 1 ? "" : "s"}`;
    elements.dashboardPreviousButton.disabled = portfolioPage.page <= 1;
    elements.dashboardNextButton.disabled =
      portfolioPage.totalPages === 0 || portfolioPage.page >= portfolioPage.totalPages;

    elements.dashboardTableBody.replaceChildren();
    if (portfolioPage.items.length === 0) {
      const emptyRow = document.createElement("tr");
      const emptyCell = createElement("td", "dashboard-empty", "No portfolios match these filters.");
      emptyCell.colSpan = 4;
      emptyRow.append(emptyCell);
      elements.dashboardTableBody.append(emptyRow);
    } else {
      for (const portfolio of portfolioPage.items) {
        const row = document.createElement("tr");
        row.className = "portfolio-row-is-expandable";
        row.tabIndex = 0;
        row.setAttribute("aria-expanded", "false");
        row.title = "Show positions";
        row.append(
          createTableCell(portfolio.name),
          createTableCell(portfolio.baseCurrency),
          createTableCell(String(portfolio.positionCount)),
          createTableCell(formatMoney(portfolio.grossExposure, portfolio.baseCurrency))
        );
        elements.dashboardTableBody.append(row);

        const detailRow = document.createElement("tr");
        detailRow.className = "portfolio-detail-row";
        detailRow.hidden = true;
        const detailCell = createElement("td", "portfolio-detail-cell");
        detailCell.colSpan = 4;
        detailCell.textContent = "Loading positions…";
        detailRow.append(detailCell);
        elements.dashboardTableBody.append(detailRow);

        const togglePositions = async () => {
          const expanded = row.getAttribute("aria-expanded") === "true";
          row.setAttribute("aria-expanded", String(!expanded));
          detailRow.hidden = expanded;
          if (expanded || detailCell.dataset.loaded === "true") return;
          try {
            const fullPortfolio = await getJson(`/api/v1/portfolios/${portfolio.id}`);
            detailCell.replaceChildren();
            if (fullPortfolio.positions.length === 0) {
              detailCell.textContent = "No positions recorded.";
            } else {
              const positions = createElement("div", "position-detail-list");
              for (const position of fullPortfolio.positions) {
                const item = createElement("span", "position-detail-item");
                item.append(
                  createElement("strong", null, position.instrumentId),
                  createElement("span", null, `${position.quantity} × ${formatMoney(position.price, fullPortfolio.baseCurrency)}`),
                  createElement("em", null, formatMoney(position.marketValue, fullPortfolio.baseCurrency))
                );
                positions.append(item);
              }
              detailCell.append(positions);
            }
            detailCell.dataset.loaded = "true";
          } catch (error) {
            detailCell.textContent = "Positions could not be loaded.";
            showError(error);
          }
        };
        row.addEventListener("click", togglePositions);
        row.addEventListener("keydown", event => {
          if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            togglePositions();
          }
        });
      }
    }

    elements.currencyBreakdown.replaceChildren();
    if (statistics.byCurrency.length === 0) {
      elements.currencyBreakdown.append(
        createElement("p", "dashboard-empty", "Create a portfolio to see grouped statistics.")
      );
    } else {
      for (const item of statistics.byCurrency) {
        const row = createElement("div", "currency-row");
        const heading = createElement("div", "currency-row-heading");
        heading.append(
          createElement("strong", null, item.baseCurrency),
          createElement("span", null, `${item.portfolioCount} portfolio${item.portfolioCount === 1 ? "" : "s"}`)
        );
        const detail = createElement(
          "span",
          "currency-row-detail",
          `${item.positionCount} position${item.positionCount === 1 ? "" : "s"}`
        );
        row.append(heading, detail);
        elements.currencyBreakdown.append(row);
      }
    }
  } catch (error) {
    showError(error);
  } finally {
    setButtonBusy(elements.refreshDashboardButton, false, "Refresh data", "↻");
  }
}

async function getJson(url) {
  const headers = { Accept: "application/json" };
  if (state.accessToken) headers.Authorization = `Bearer ${state.accessToken}`;
  const response = await fetch(url, {
    headers,
    cache: "no-store"
  });
  const body = await response.json().catch(() => null);

  if (!response.ok) {
    throw new ApiError(
      response.status,
      body?.title ?? `Request failed with status ${response.status}`,
      body?.detail ?? "The server did not return additional error details.",
      body?.errors ? Object.values(body.errors).flat().map(String) : []
    );
  }

  return body;
}

async function sendJson(url, method, payload) {
  const headers = {
    Accept: "application/json",
    "Content-Type": "application/json"
  };
  if (state.accessToken) headers.Authorization = `Bearer ${state.accessToken}`;
  const response = await fetch(url, {
    method,
    headers,
    body: JSON.stringify(payload)
  });

  const body = await response.json().catch(() => null);

  if (!response.ok) {
    const validationErrors = body?.errors
      ? Object.values(body.errors).flat().map(String)
      : [];

    throw new ApiError(
      response.status,
      body?.title ?? `Request failed with status ${response.status}`,
      body?.detail ?? "The server did not return additional error details.",
      validationErrors
    );
  }

  return body;
}

async function acquireDevelopmentToken(userName = elements.loginUser.value, role = elements.loginRole.value) {
  // The endpoint exists only in Development/Testing; production should use the
  // organization's OIDC login flow instead of minting a browser token here.
  const response = await fetch("/api/v1/auth/token", {
    method: "POST",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify({ userName, role })
  });
  if (response.ok) {
    const body = await response.json();
    state.accessToken = body.accessToken;
    elements.authStatus.textContent = `Signed in as ${userName} · ${role}.`;
    return true;
  }
  elements.authStatus.textContent = "Sign-in is available only in Development/Testing.";
  return false;
}

function signOut() {
  state.accessToken = null;
  elements.authStatus.textContent = "Signed out. Choose a role to sign in again.";
}

async function checkHealth() {
  try {
    const response = await fetch("/health", {
      headers: { Accept: "text/plain" },
      cache: "no-store"
    });

    if (!response.ok) {
      throw new Error("Health endpoint did not return success.");
    }

    elements.apiState.classList.add("is-online");
    elements.apiState.classList.remove("is-offline");
    elements.apiStatusText.textContent = "API online";
  } catch {
    elements.apiState.classList.add("is-offline");
    elements.apiState.classList.remove("is-online");
    elements.apiStatusText.textContent = "API unavailable";
  }
}

function showError(error) {
  const normalized =
    error instanceof ApiError
      ? error
      : new ApiError(
          0,
          "The request could not be completed",
          error instanceof Error ? error.message : "An unexpected browser error occurred."
        );

  elements.errorTitle.textContent =
    normalized.status > 0
      ? `${normalized.title} · HTTP ${normalized.status}`
      : normalized.title;
  elements.errorMessage.textContent = normalized.message;
  elements.errorDetails.replaceChildren();

  for (const detail of normalized.validationErrors) {
    elements.errorDetails.append(createElement("li", null, detail));
  }

  elements.errorDetails.hidden = normalized.validationErrors.length === 0;
  elements.errorAlert.hidden = false;
  elements.errorAlert.scrollIntoView({
    behavior: prefersReducedMotion() ? "auto" : "smooth",
    block: "center"
  });
}

function hideError() {
  elements.errorAlert.hidden = true;
}

function showToast(message) {
  elements.toast.textContent = message;
  elements.toast.hidden = false;
  window.clearTimeout(showToast.timeoutId);
  showToast.timeoutId = window.setTimeout(() => {
    elements.toast.hidden = true;
  }, 3200);
}

function setButtonBusy(button, isBusy, label, icon = "→") {
  button.disabled = isBusy;
  button.classList.toggle("is-busy", isBusy);
  button.replaceChildren();

  if (isBusy) {
    const spinner = createElement("span");
    spinner.setAttribute("aria-hidden", "true");
    button.append(spinner, document.createTextNode(label));
  } else {
    button.append(
      document.createTextNode(label),
      createElement("span", null, icon)
    );
  }
}

function setJourneyState(activeStep) {
  const order = ["portfolio", "scenarios", "report"];
  const activeIndex = order.indexOf(activeStep);

  for (const step of document.querySelectorAll("[data-journey-step]")) {
    const stepIndex = order.indexOf(step.dataset.journeyStep);
    step.classList.toggle("is-current", stepIndex === activeIndex);
    step.classList.toggle("is-complete", stepIndex < activeIndex);
  }
}

function resetForAnotherPortfolio() {
  state.portfolio = null;
  elements.portfolioForm.reset();
  elements.portfolioForm.hidden = false;
  elements.portfolioSummary.hidden = true;
  elements.portfolioPanelState.textContent = "Draft";
  elements.portfolioPanelState.classList.remove("is-ready");
  elements.riskForm.hidden = true;
  elements.riskLockedState.hidden = false;
  elements.scenarioPanelState.textContent = "Locked";
  elements.scenarioPanelState.classList.remove("is-ready");
  elements.riskReport.hidden = true;
  elements.positionList.replaceChildren();
  addPositionRow({ instrumentId: "", quantity: 0, price: 0 });
  setJourneyState("portfolio");
  elements.portfolioName.focus();
}

function formatMoney(value, currency) {
  try {
    return new Intl.NumberFormat(undefined, {
      style: "currency",
      currency,
      currencyDisplay: "narrowSymbol",
      maximumFractionDigits: 2
    }).format(value);
  } catch {
    return `${new Intl.NumberFormat(undefined, {
      maximumFractionDigits: 2
    }).format(value)} ${currency}`;
  }
}

function formatPercent(value) {
  return `${new Intl.NumberFormat(undefined, {
    maximumFractionDigits: 2
  }).format(value)}%`;
}

function formatCompactNumber(value) {
  return new Intl.NumberFormat(undefined, {
    notation: "compact",
    maximumFractionDigits: 1,
    signDisplay: "exceptZero"
  }).format(value);
}

function formatShortDate(value) {
  return new Intl.DateTimeFormat(undefined, {
    month: "short",
    day: "numeric",
    timeZone: "UTC"
  }).format(new Date(`${value}T00:00:00Z`));
}

function formatLongDate(value) {
  return new Intl.DateTimeFormat(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
    timeZone: "UTC"
  }).format(new Date(`${value}T00:00:00Z`));
}

function prefersReducedMotion() {
  return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

elements.loadSampleButton.addEventListener("click", () => {
  // The hero action is always available, including after a completed report.
  // Reset server-linked browser state before presenting the guided draft again.
  resetForAnotherPortfolio();
  fillPortfolioForm(samplePortfolio);
  setJourneyState("portfolio");
  window.location.hash = "workbench";
  setView("workbench");
  document.querySelector(".portfolio-panel").scrollIntoView({
    behavior: prefersReducedMotion() ? "auto" : "smooth",
    block: "start"
  });
  showToast("Guided AAPL/MSFT example loaded.");
});
elements.addPositionButton.addEventListener("click", () => addPositionRow());
elements.portfolioForm.addEventListener("submit", submitPortfolio);
elements.editPortfolioButton.addEventListener("click", resetForAnotherPortfolio);
elements.addScenarioButton.addEventListener("click", () => addScenarioRow());
elements.riskForm.addEventListener("submit", submitRisk);
elements.queueRiskButton.addEventListener("click", submitQueuedRisk);
elements.dismissErrorButton.addEventListener("click", hideError);
elements.refreshDashboardButton.addEventListener("click", () => loadDashboard());
elements.dashboardSearchButton.addEventListener("click", () => {
  state.dashboardPage = 1;
  loadDashboard(1);
});
elements.dashboardPreviousButton.addEventListener("click", () => {
  if (state.dashboardPage > 1) loadDashboard(state.dashboardPage - 1);
});
elements.dashboardNextButton.addEventListener("click", () => {
  if (state.dashboardPage < state.dashboardTotalPages) {
    loadDashboard(state.dashboardPage + 1);
  }
});
elements.dashboardCurrencyFilter.addEventListener("input", event => {
  event.target.value = event.target.value.toUpperCase();
});
elements.viewLinks.forEach(link => {
  link.addEventListener("click", () => setView(link.dataset.viewLink));
});
elements.loginForm.addEventListener("submit", async event => {
  event.preventDefault();
  try {
    await acquireDevelopmentToken();
    await loadDashboard();
  } catch (error) {
    showError(error);
  }
});
elements.logoutButton.addEventListener("click", signOut);
window.addEventListener("hashchange", setViewFromHash);
elements.baseCurrency.addEventListener("input", event => {
  event.target.value = event.target.value.toUpperCase();
});

// Populate a useful first-run example without stealing keyboard focus on page load.
fillPortfolioForm(samplePortfolio, false);
setJourneyState("portfolio");
setViewFromHash();
checkHealth();
acquireDevelopmentToken().finally(() => loadDashboard());
