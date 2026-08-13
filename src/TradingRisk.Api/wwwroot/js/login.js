const form = document.querySelector("#login-form");
const error = document.querySelector("#login-error");

form.addEventListener("submit", async event => {
  event.preventDefault();
  error.hidden = true;
  const button = form.querySelector("button[type=submit]");
  button.disabled = true;
  button.textContent = "Signing in…";
  try {
    const response = await fetch("/api/v1/auth/token", {
      method: "POST",
      headers: { Accept: "application/json", "Content-Type": "application/json" },
      body: JSON.stringify({
        userName: document.querySelector("#login-user").value,
        role: document.querySelector("#login-role").value
      })
    });
    const body = await response.json().catch(() => null);
    if (!response.ok) throw new Error(body?.detail || "Sign-in is unavailable.");
    sessionStorage.setItem("riskengine.accessToken", body.accessToken);
    sessionStorage.setItem("riskengine.user", document.querySelector("#login-user").value);
    sessionStorage.setItem("riskengine.role", document.querySelector("#login-role").value);
    window.location.replace("/");
  } catch (caught) {
    error.textContent = caught.message;
    error.hidden = false;
    button.disabled = false;
    button.textContent = "Sign in ↗";
  }
});
