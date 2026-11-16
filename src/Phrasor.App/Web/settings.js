// Settings page, General and Model groups. Edits are a draft: nothing is stored until Save, and Discard puts back what the app has.
// The app never sends a key to this page, only "set or not set". A key typed here is sent once, in Save or Test connection.
// All text from the app goes in with textContent. The rules (limits, allowed values) live in the app; this only mirrors them for convenience.
(function () {
  "use strict";

  var $ = function (id) { return document.getElementById(id); };
  var view = null;          // what the app last said
  var baseline = "";        // the draft that matches it
  var typed = {};           // keys typed this session, by provider id
  var removed = {};         // saved keys the user marked for removal
  var keyRows = {};         // provider id -> { status, input, remove }
  var testing = false, currentTest = null, saving = false, loading = false;
  var themeRadios = document.querySelectorAll("input[name=theme]");

  var ERROR_TEXT = {
    bad_request: "Some settings are not valid. Check the model name and the address.",
    too_large: "That is too long. Shorten the About me text.",
    rate_limited: "Too many requests. Wait a moment and try again.",
    busy: "A connection test is already running.",
    timeout: "The app did not answer in time. Try again."
  };

  function el(tag, className, text) {
    var node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  function providerOf(id) {
    return view.providers.filter(function (p) { return p.id === id; })[0] || view.providers[0];
  }

  function fillSelect(select, options, value) {
    select.replaceChildren();
    options.forEach(function (o) {
      var option = el("option", "", o.label);
      option.value = o.value;
      select.appendChild(option);
    });
    select.value = value;
  }

  function isKnownDefault(model) {
    return view.providers.some(function (p) { return p.defaultModel === model || p.defaultFallbackModel === model; });
  }

  // Shows the address box only for providers that take one, and the provider's note under the main model.
  function syncProvider(prefix) {
    var p = providerOf($(prefix + "-provider").value);
    $(prefix + "-url-row").hidden = !p.urlEditable;
    if (prefix === "p") $("p-notes").textContent = p.notes;
  }

  function onProviderChange(prefix) {
    var p = providerOf($(prefix + "-provider").value);
    var model = $(prefix + "-model");
    var fallback = prefix === "f";
    if (model.value.trim() === "" || isKnownDefault(model.value.trim())) model.value = fallback ? p.defaultFallbackModel : p.defaultModel;
    syncProvider(prefix);
    update();
  }

  function syncFallback() {
    var on = $("f-on").checked;
    ["f-provider", "f-model", "f-url"].forEach(function (id) { $(id).disabled = !on; });
  }

  function render(v) {
    view = v;
    typed = {};
    removed = {};
    var g = v.general;
    themeRadios.forEach(function (r) { r.checked = r.value === g.theme; });
    fillSelect($("set-mode"), v.modes.map(function (m) { return { value: m, label: m }; }), g.defaultMode);
    $("set-review").checked = g.reviewBeforeReplace;
    $("set-startup").checked = g.startWithWindows;
    $("set-apphint").checked = g.sendAppHint;
    $("set-repair").checked = g.repairDroppedDetails;
    $("set-updates").checked = g.checkForUpdates;
    $("set-about").value = g.personalContext;

    var providers = v.providers.map(function (p) { return { value: p.id, label: p.name }; });
    fillSelect($("p-provider"), providers, v.primary.provider);
    $("p-model").value = v.primary.model;
    $("p-url").value = v.primary.baseUrl;
    fillSelect($("f-provider"), providers, v.fallback.provider);
    $("f-model").value = v.fallback.model;
    $("f-url").value = v.fallback.baseUrl;
    $("f-on").checked = v.fallback.enabled;
    syncProvider("p");
    syncProvider("f");
    syncFallback();
    renderKeys();
    $("set-test-result").textContent = "";
    $("set-test-result").setAttribute("data-tone", "");
    baseline = JSON.stringify(readDraft());
    update();
  }

  function renderKeys() {
    var box = $("keys");
    box.replaceChildren();
    keyRows = {};
    view.keys.forEach(function (k) {
      var row = el("div", "sr key");
      var label = el("span", "st", k.name);
      var status = el("small", "");
      label.appendChild(status);
      var tools = el("div", "ktools");
      var input = el("input", "txt");
      input.type = "password";
      input.setAttribute("autocomplete", "new-password");
      input.setAttribute("spellcheck", "false");
      input.setAttribute("maxlength", "500");
      input.setAttribute("aria-label", k.name + " API key");
      input.placeholder = k.set ? "Saved. Type to replace." : "Paste your key";
      input.addEventListener("input", function () {
        var value = input.value.trim();
        if (value) { typed[k.id] = value; delete removed[k.id]; } else delete typed[k.id];
        update();
      });
      var remove = el("button", "btn", "Remove");
      remove.type = "button";
      remove.addEventListener("click", function () {
        if (removed[k.id]) delete removed[k.id];
        else { removed[k.id] = true; delete typed[k.id]; input.value = ""; }
        update();
      });
      tools.appendChild(input);
      tools.appendChild(remove);
      if (k.linkId) {
        var get = el("button", "btn link", "Get a key");
        get.type = "button";
        get.addEventListener("click", function () { window.phrasor.openLink(k.linkId).catch(function () {}); });
        tools.appendChild(get);
      }
      row.appendChild(label);
      row.appendChild(tools);
      box.appendChild(row);
      keyRows[k.id] = { status: status, input: input, remove: remove, set: k.set };
    });
  }

  function syncKeys() {
    Object.keys(keyRows).forEach(function (id) {
      var r = keyRows[id];
      var text = removed[id] ? "Will be removed when you save." : typed[id] ? "New key, not saved yet." : r.set ? "Saved." : "Not set.";
      r.status.textContent = text;
      r.status.setAttribute("data-tone", removed[id] ? "warn" : typed[id] ? "warn" : r.set ? "ok" : "");
      r.remove.hidden = !(r.set || removed[id]);
      r.remove.textContent = removed[id] ? "Undo remove" : "Remove";
    });
  }

  function endpoint(prefix, withEnabled) {
    var o = { provider: $(prefix + "-provider").value, model: $(prefix + "-model").value.trim(), baseUrl: $(prefix + "-url").value.trim() };
    if (withEnabled) o.enabled = $("f-on").checked;
    return o;
  }

  function readDraft() {
    var theme = "system";
    themeRadios.forEach(function (r) { if (r.checked) theme = r.value; });
    return {
      general: {
        theme: theme,
        defaultMode: $("set-mode").value,
        reviewBeforeReplace: $("set-review").checked,
        startWithWindows: $("set-startup").checked,
        sendAppHint: $("set-apphint").checked,
        repairDroppedDetails: $("set-repair").checked,
        checkForUpdates: $("set-updates").checked,
        personalContext: $("set-about").value
      },
      model: { primary: endpoint("p", false), fallback: endpoint("f", true) },
      keys: { set: typed, remove: Object.keys(removed) }
    };
  }

  function problem(draft) {
    if (draft.model.primary.model === "") return "Enter a model name for the main model.";
    return "";
  }

  function update() {
    syncKeys();
    syncFallback();
    var dirty = view !== null && JSON.stringify(readDraft()) !== baseline;
    var bad = view ? problem(readDraft()) : "";
    $("set-save").disabled = !dirty || saving || bad !== "";
    $("set-discard").disabled = !dirty || saving;
    $("set-test").disabled = testing || saving || view === null;
    $("set-state").textContent = saving ? "Saving…" : bad !== "" && dirty ? bad : dirty ? "You have unsaved changes." : "No changes.";
  }

  function showError(text) {
    var node = $("set-error");
    node.textContent = text;
    node.hidden = text === "";
  }

  function load() {
    if (loading) return;
    loading = true;
    window.phrasor.settingsGet().then(function (v) {
      loading = false;
      showError("");
      render(v);
    }, function () {
      loading = false;
      showError("Settings could not be loaded. Close and reopen this page to try again.");
    });
  }

  $("set-save").addEventListener("click", function () {
    var draft = readDraft();
    if (problem(draft)) return;
    saving = true;
    showError("");
    update();
    window.phrasor.settingsSave(draft).then(function (v) {
      saving = false;
      render(v);
      $("set-state").textContent = "Saved.";
      window.dispatchEvent(new Event("phrasor:settings-saved"));
    }, function (error) {
      saving = false;
      showError(ERROR_TEXT[error.message] || "Settings could not be saved. Nothing was changed. Try again.");
      update();
    });
  });

  $("set-discard").addEventListener("click", function () {
    showError("");
    render(view);
  });

  $("set-test").addEventListener("click", function () {
    var draft = readDraft();
    var result = $("set-test-result");
    if (problem(draft)) { result.textContent = problem(draft); result.setAttribute("data-tone", "warn"); return; }
    testing = true;
    currentTest = null;
    result.textContent = "Testing…";
    result.setAttribute("data-tone", "");
    update();
    window.phrasor.settingsTest(draft).then(function (reply) { currentTest = reply.run; }, function (error) {
      testing = false;
      result.textContent = ERROR_TEXT[error.message] || "The test could not start. Try again.";
      result.setAttribute("data-tone", "bad");
      update();
    });
  });

  window.phrasor.onSettingsTest(function (event) {
    if (event.run !== currentTest) return; // a result from an older test
    testing = false;
    var result = $("set-test-result");
    if (event.ok) { result.textContent = "Connected to " + event.label + " in " + event.ms + " ms."; result.setAttribute("data-tone", "ok"); }
    else { result.textContent = event.message; result.setAttribute("data-tone", "bad"); }
    update();
  });

  $("f-on").addEventListener("change", update);
  $("p-provider").addEventListener("change", function () { onProviderChange("p"); });
  $("f-provider").addEventListener("change", function () { onProviderChange("f"); });
  document.querySelector("[data-page=settings]").addEventListener("input", function (event) {
    if (event.target.type !== "password") { $("set-test-result").textContent = ""; update(); }
  });
  document.querySelector("[data-page=settings]").addEventListener("change", update);

  // Reload when the page is opened, unless there are edits that would be lost.
  document.querySelectorAll("[data-nav=settings]").forEach(function (button) {
    button.addEventListener("click", function () { if (!view || JSON.stringify(readDraft()) === baseline) load(); });
  });
  load();
})();
