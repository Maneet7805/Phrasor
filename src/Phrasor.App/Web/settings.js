// Settings page: General, Model, Shortcuts, Voice and Requirements. Edits are a draft: nothing is stored until Save, and Discard puts back what the app has.
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
    too_large: "That is too long. Shorten the About me text or a requirements text.",
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

  // ---- Shortcuts: a recorder per chord. The app releases the global shortcuts while one is recording, so the keys reach this page. ----
  var SHORTCUTS = [
    { id: "optimize", key: "optimize", label: "Rewrite", hint: "Selects everything in the text box you are typing in, rewrites it, and replaces it when you accept." },
    { id: "selection", key: "optimizeSelection", label: "Selection only", hint: "Only the selected text. Use it in documents and code editors, where select-all would take the whole file." },
    { id: "voice", key: "voice", label: "Dictate", hint: "Tap once to start and again to stop, or hold the keys while you talk and release to finish." }
  ];
  var chords = {};          // draft chord by shortcut key
  var shortcutRows = {};    // shortcut id -> { button, note, clear }
  var recordingId = null;
  var notes = {};           // shortcut id -> { text, tone }
  var reqs = {};            // draft requirement text by task id
  var recordingBusy = false;

  // Browser key codes to the key names the app's shortcut parser knows.
  var NAMED_KEYS = {
    Space: "Space", Enter: "Enter", Insert: "Insert", Delete: "Delete", Home: "Home", End: "End", PageUp: "PageUp", PageDown: "PageDown",
    ArrowLeft: "Left", ArrowRight: "Right", ArrowUp: "Up", ArrowDown: "Down", Minus: "OemMinus", Equal: "Oemplus", Comma: "Oemcomma",
    Period: "OemPeriod", Slash: "OemQuestion", Semicolon: "OemSemicolon", Quote: "OemQuotes", BracketLeft: "OemOpenBrackets",
    BracketRight: "OemCloseBrackets", Backslash: "OemPipe", Backquote: "Oemtilde"
  };

  function keyNameOf(code) {
    var m;
    if ((m = /^Key([A-Z])$/.exec(code))) return m[1];
    if ((m = /^Digit([0-9])$/.exec(code))) return m[1];
    if (/^F([1-9]|1[0-9]|2[0-4])$/.test(code)) return code;
    return NAMED_KEYS[code] || "";
  }

  function setNote(id, text, tone) {
    notes[id] = text ? { text: text, tone: tone || "bad" } : null;
    var row = shortcutRows[id];
    if (!row) return;
    var n = notes[id];
    row.note.textContent = n ? n.text : "";
    row.note.setAttribute("data-tone", n ? n.tone : "");
  }

  function renderShortcuts() {
    var box = $("shortcuts");
    box.replaceChildren();
    shortcutRows = {};
    SHORTCUTS.forEach(function (s) {
      var row = el("div", "sr key rec");
      var label = el("span", "st", s.label);
      var note = el("small", "", "");
      note.setAttribute("role", "status");
      label.appendChild(el("small", "mute", s.hint));
      label.appendChild(note);
      var tools = el("div", "rtools");
      var button = el("button", "rbtn");
      button.type = "button";
      button.setAttribute("aria-label", s.label + " shortcut. Press Enter to record a new one.");
      button.addEventListener("click", function () { startRecording(s.id); });
      button.addEventListener("keydown", function (event) { onRecordKey(event, s); });
      button.addEventListener("blur", function () { if (recordingId === s.id) stopRecording(); });
      var clear = el("button", "btn", "Clear");
      clear.type = "button";
      clear.addEventListener("click", function () { chords[s.key] = ""; setNote(s.id, ""); syncShortcuts(); update(); });
      tools.appendChild(button);
      tools.appendChild(clear);
      row.appendChild(label);
      row.appendChild(tools);
      box.appendChild(row);
      shortcutRows[s.id] = { button: button, note: note, clear: clear };
    });
    syncShortcuts();
  }

  function syncShortcuts() {
    SHORTCUTS.forEach(function (s) {
      var row = shortcutRows[s.id];
      if (!row) return;
      var recording = recordingId === s.id;
      row.button.textContent = recording ? "Press the keys you want…" : chords[s.key] || "Not set";
      row.button.setAttribute("aria-pressed", recording ? "true" : "false");
      row.clear.hidden = !chords[s.key];
    });
  }

  function startRecording(id) {
    if (recordingId === id) return;
    var previous = recordingId;
    recordingId = id;
    setNote(id, "");
    syncShortcuts();
    if (previous === null) window.phrasor.shortcutRecordStart().catch(function () {
      recordingId = null;
      setNote(id, "Recording could not start. Try again.");
      syncShortcuts();
    });
  }

  // Safe to call at any time: puts the app's shortcuts back. A failure here is retried by the app when the window closes or loses focus.
  function stopRecording() {
    if (recordingId === null) return;
    recordingId = null;
    syncShortcuts();
    window.phrasor.shortcutRecordStop().catch(function () {});
  }

  function onRecordKey(event, s) {
    if (recordingId !== s.id) return;
    if (event.key === "Tab") return;                       // let the user move on; blur stops recording
    event.preventDefault();
    event.stopPropagation();
    if (event.key === "Escape") { stopRecording(); return; }
    if (event.key === "Backspace") { chords[s.key] = ""; stopRecording(); update(); return; }
    if (["Control", "Alt", "Shift", "Meta", "AltGraph"].indexOf(event.key) >= 0) return; // waiting for the main key
    var name = keyNameOf(event.code);
    if (name === "") { setNote(s.id, "That key cannot be used. Try another.", "warn"); return; }
    if (!event.ctrlKey && !event.altKey) { setNote(s.id, "Include Ctrl or Alt so normal typing cannot trigger it.", "warn"); return; }
    chords[s.key] = (event.ctrlKey ? "Ctrl+" : "") + (event.altKey ? "Alt+" : "") + (event.shiftKey ? "Shift+" : "") + name;
    setNote(s.id, "");
    stopRecording();
    update();
  }

  // ---- Voice ----
  function syncVoice() {
    var on = $("v-on").checked;
    ["v-provider", "v-model", "v-lang", "v-max", "v-auto"].forEach(function (id) { $(id).disabled = !on; });
    $("v-notes").textContent = $("v-provider").value === "custom" ? "Custom uses the address of the main model above." : "Groq's free plan includes Whisper transcription with the same API key.";
  }

  function readVoice() {
    var seconds = parseInt($("v-max").value, 10);
    return {
      enabled: $("v-on").checked, provider: $("v-provider").value, model: $("v-model").value.trim(), language: $("v-lang").value.trim(),
      autoOptimize: $("v-auto").checked, maxSeconds: isNaN(seconds) ? 0 : seconds
    };
  }

  // ---- Requirements: one box shown at a time, all kept in the draft ----
  function showRequirement() {
    $("r-text").value = reqs[$("r-task").value] || "";
    syncCount();
  }

  function syncCount() {
    var length = $("r-text").value.trim().length;
    var over = length > view.requirementsMax;
    $("r-count").textContent = length + " of " + view.requirementsMax + " characters" + (over ? ". Too long: shorten it." : ".");
    $("r-count").setAttribute("data-tone", over ? "bad" : "");
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
    stopRecording();
    notes = {};
    chords = { optimize: v.shortcuts.optimize, optimizeSelection: v.shortcuts.optimizeSelection, voice: v.shortcuts.voice };
    renderShortcuts();
    var vo = v.voice;
    $("v-on").checked = vo.enabled;
    fillSelect($("v-provider"), v.voiceProviders.map(function (p) { return { value: p.id, label: p.name }; }), vo.provider);
    $("v-model").value = vo.model;
    $("v-lang").value = vo.language;
    $("v-max").value = String(vo.maxSeconds);
    $("v-auto").checked = vo.autoOptimize;
    syncVoice();
    reqs = {};
    v.requirements.forEach(function (r) { reqs[r.id] = r.text; });
    var wanted = $("r-task").value;
    fillSelect($("r-task"), v.requirements.map(function (r) { return { value: r.id, label: r.label }; }), reqs[wanted] !== undefined ? wanted : v.requirements[0].id);
    $("r-text").setAttribute("maxlength", String(v.requirementsMax));
    showRequirement();
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
      keys: { set: typed, remove: Object.keys(removed) },
      shortcuts: { optimize: chords.optimize, optimizeSelection: chords.optimizeSelection, voice: chords.voice },
      voice: readVoice(),
      requirements: reqs
    };
  }

  function problem(draft) {
    if (draft.model.primary.model === "") return "Enter a model name for the main model.";
    if (draft.voice.enabled && draft.voice.model === "") return "Enter a model name for voice, or turn voice off.";
    if (draft.voice.maxSeconds < 10 || draft.voice.maxSeconds > 600) return "Voice can stop after 10 to 600 seconds.";
    if (view && Object.keys(draft.requirements).some(function (id) { return draft.requirements[id].trim().length > view.requirementsMax; })) return "A requirements text is too long.";
    return "";
  }

  function update() {
    syncKeys();
    syncFallback();
    if (view) { syncVoice(); syncCount(); }
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
    stopRecording();
    window.phrasor.settingsSave(draft).then(function (reply) {
      saving = false;
      if (!reply.saved) {
        // Nothing was saved. Keep the draft and say what is wrong with which shortcut.
        reply.problems.forEach(function (p) { setNote(p.id, p.message, "bad"); });
        update();
        $("set-state").textContent = "Fix the shortcut above, then save again.";
        return;
      }
      render(reply.settings);
      reply.warnings.forEach(function (w) { setNote(w.id, w.message, "warn"); });
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
    stopRecording();
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
  $("r-task").addEventListener("change", showRequirement);
  $("r-text").addEventListener("input", function () { reqs[$("r-task").value] = $("r-text").value; });
  // Leaving the page or the window must put the app's shortcuts back.
  window.addEventListener("blur", stopRecording);
  document.querySelectorAll("[data-nav]").forEach(function (button) { button.addEventListener("click", stopRecording); });
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
