// Settings page: General, Models, Review window, Shortcuts, Voice and Requirements. Edits are a draft: nothing is stored until Save, and Discard puts back what the app has.
// The app never sends a key to this page, only "set or not set". A key typed here is sent once, in Save or Test connection.
// All text from the app goes in with textContent. The rules (limits, allowed values) live in the app; this only mirrors them for convenience.
(function () {
  "use strict";

  var $ = function (id) { return document.getElementById(id); };
  var view = null;          // what the app last said
  var baseline = "";        // the draft that matches it
  var typed = {};           // keys typed this session, by key id: a provider id, or "model:<id>" for a model with its own key
  var removed = {};         // saved provider keys the user marked for removal
  var models = [];          // the draft model list: default first, then backups, then the rest
  var defaultId = "";
  var backupOn = {};        // model id -> true when it is a backup
  var order = [];           // draft task order (every task but Auto)
  var modelRows = {};       // model id -> { status, model }
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

  function keyIdOf(m) { return "model:" + m.id; }

  // New models get an id made here; the app checks it (1 to 12 lowercase letters and digits).
  function newId() {
    var chars = "abcdefghijklmnopqrstuvwxyz0123456789", id;
    do {
      id = "n";
      for (var i = 0; i < 6; i++) id += chars.charAt(Math.floor(Math.random() * chars.length));
    } while (models.some(function (m) { return m.id === id; }));
    return id;
  }

  // Mirrors the app's rule for "runs on this PC": Ollama, or a custom address that points at this machine.
  function isLocal(m) {
    if (m.provider === "ollama") return true;
    if (m.provider !== "custom") return false;
    return /^https?:\/\/(localhost|127\.\d+\.\d+\.\d+|\[::1\])([:\/]|$)/i.test(m.baseUrl.trim());
  }

  function backupList() {
    return models.filter(function (m) { return m.id !== defaultId && backupOn[m.id]; }).map(function (m) { return m.id; });
  }

  function move(list, index, by) {
    var to = index + by;
    if (to < 0 || to >= list.length) return;
    var item = list.splice(index, 1)[0];
    list.splice(to, 0, item);
  }

  function sortModels(list, def, backups) {
    var rank = function (m) { return m.id === def ? 0 : backups.indexOf(m.id) >= 0 ? 1 + backups.indexOf(m.id) : 100; };
    return list.map(function (m, i) { return { m: m, i: i }; })
      .sort(function (a, b) { return rank(a.m) - rank(b.m) || a.i - b.i; })
      .map(function (x) { return x.m; });
  }

  function renderModels() {
    var box = $("models");
    box.replaceChildren();
    modelRows = {};
    models.forEach(function (m, index) {
      var isDef = m.id === defaultId;
      var row = el("div", "sr col model");
      var head = el("div", "mhead");
      var title = el("span", "st", isDef ? "Default model" : backupOn[m.id] ? "Backup model" : "Saved model");
      var chip = el("small", "", isLocal(m) ? "Runs on this PC. Not tested here." : "");
      title.appendChild(chip);
      head.appendChild(title);
      var tools = el("div", "rtools");
      var btn = function (text, label, onClick, disabled) {
        var b = el("button", "btn", text);
        b.type = "button";
        b.setAttribute("aria-label", label);
        b.disabled = !!disabled;
        b.addEventListener("click", onClick);
        tools.appendChild(b);
      };
      if (!isDef) btn("Make default", "Make this model the default", function () { defaultId = m.id; backupOn[m.id] = false; refreshModels(); });
      if (!isDef) btn(backupOn[m.id] ? "Not a backup" : "Use as backup", backupOn[m.id] ? "Stop using this model as a backup" : "Use this model as a backup", function () { backupOn[m.id] = !backupOn[m.id]; refreshModels(); });
      btn("Up", "Move this model up", function () { move(models, index, -1); refreshModels(); }, index === 0);
      btn("Down", "Move this model down", function () { move(models, index, 1); refreshModels(); }, index === models.length - 1);
      btn("Remove", "Remove this model", function () {
        models = models.filter(function (x) { return x.id !== m.id; });
        delete typed[keyIdOf(m)];
        refreshModels();
      }, isDef || models.length <= 1);
      head.appendChild(tools);
      row.appendChild(head);

      var fields = el("div", "fields");
      var pLabel = el("label", "", "Provider");
      var pSel = el("select");
      view.providers.forEach(function (p) { var o = el("option", "", p.name); o.value = p.id; pSel.appendChild(o); });
      pSel.value = m.provider;
      pLabel.appendChild(pSel);
      var mLabel = el("label", "", "Model");
      var mIn = el("input", "txt");
      mIn.type = "text"; mIn.maxLength = 100; mIn.value = m.model;
      mIn.setAttribute("spellcheck", "false"); mIn.setAttribute("autocomplete", "off");
      mLabel.appendChild(mIn);
      var uLabel = el("label", "wide", "Address");
      var uIn = el("input", "txt");
      uIn.type = "text"; uIn.maxLength = 200; uIn.value = m.baseUrl;
      uIn.placeholder = "Address of the API (custom and local only)";
      uIn.setAttribute("spellcheck", "false"); uIn.setAttribute("autocomplete", "off");
      uLabel.appendChild(uIn);
      uLabel.hidden = !providerOf(m.provider).urlEditable;
      fields.appendChild(pLabel); fields.appendChild(mLabel); fields.appendChild(uLabel);
      row.appendChild(fields);
      var note = el("small", "mute", providerOf(m.provider).notes);
      row.appendChild(note);

      pSel.addEventListener("change", function () {
        var p = providerOf(pSel.value);
        var cur = mIn.value.trim();
        m.provider = p.id;
        if (cur === "" || isKnownDefault(cur)) { mIn.value = isDef ? p.defaultModel : p.defaultFallbackModel || p.defaultModel; m.model = mIn.value; }
        uLabel.hidden = !p.urlEditable;
        note.textContent = p.notes;
        chip.textContent = isLocal(m) ? "Runs on this PC. Not tested here." : "";
        update();
      });
      mIn.addEventListener("input", function () { m.model = mIn.value.trim(); update(); });
      uIn.addEventListener("input", function () { m.baseUrl = uIn.value.trim(); chip.textContent = isLocal(m) ? "Runs on this PC. Not tested here." : ""; update(); });

      // Key choice: reuse the provider's key, or give this model its own. A saved key is never shown.
      var keyBox = el("div", "mkey");
      var keyLabel = el("label", "inl");
      var sep = el("input", "chk");
      sep.type = "checkbox"; sep.checked = m.separateKey;
      keyLabel.appendChild(sep);
      keyLabel.appendChild(el("span", "st", "Use a separate key for this model"));
      keyBox.appendChild(keyLabel);
      var kIn = el("input", "txt");
      kIn.type = "password"; kIn.maxLength = 500; kIn.hidden = !m.separateKey;
      kIn.setAttribute("autocomplete", "new-password"); kIn.setAttribute("spellcheck", "false");
      kIn.setAttribute("aria-label", "API key for this model");
      kIn.placeholder = m.keySet ? "Saved. Type to replace." : "Paste the key for this model";
      var status = el("small", "");
      keyBox.appendChild(kIn);
      keyBox.appendChild(status);
      row.appendChild(keyBox);
      sep.addEventListener("change", function () {
        m.separateKey = sep.checked;
        kIn.hidden = !sep.checked;
        if (!sep.checked) { delete typed[keyIdOf(m)]; kIn.value = ""; }
        update();
      });
      kIn.addEventListener("input", function () {
        var value = kIn.value.trim();
        if (value) typed[keyIdOf(m)] = value; else delete typed[keyIdOf(m)];
        update();
      });
      modelRows[m.id] = { status: status, model: m };
      box.appendChild(row);
    });
    $("m-add").disabled = models.length >= view.models.max;
    $("m-limit").textContent = models.length >= view.models.max ? "You have reached the limit of " + view.models.max + " models." : "";
  }

  function syncModels() {
    Object.keys(modelRows).forEach(function (id) {
      var r = modelRows[id], m = r.model;
      var text = !m.separateKey ? "Uses the " + providerOf(m.provider).name + " key below." : typed[keyIdOf(m)] ? "New key, not saved yet." : m.keySet ? "Saved." : "No key yet.";
      r.status.textContent = text;
      r.status.setAttribute("data-tone", m.separateKey && !typed[keyIdOf(m)] && !m.keySet ? "warn" : m.separateKey ? "ok" : "");
    });
  }

  function refreshModels() { renderModels(); update(); }

  // ---- Review window: the order of the task bar ----
  function readCount() {
    var n = parseInt($("rv-count").value, 10);
    return isNaN(n) ? 0 : n;
  }

  function renderOrder() {
    var box = $("rv-order");
    box.replaceChildren();
    var count = readCount();
    order.forEach(function (task, index) {
      // The bar holds Auto plus (count - 1) tasks; the dashed line sits under the last one that fits.
      var row = el("div", "orow" + (index === count - 2 ? " cut" : ""));
      row.appendChild(el("span", "", task));
      var tools = el("div", "rtools");
      [["Up", -1], ["Down", 1]].forEach(function (d) {
        var b = el("button", "btn", d[0]);
        b.type = "button";
        b.setAttribute("aria-label", "Move " + task + " " + d[0].toLowerCase());
        b.disabled = d[1] < 0 ? index === 0 : index === order.length - 1;
        b.addEventListener("click", function () { move(order, index, d[1]); renderOrder(); update(); });
        tools.appendChild(b);
      });
      row.appendChild(tools);
      box.appendChild(row);
    });
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

    models = sortModels(v.models.saved.map(function (m) {
      return { id: m.id, provider: m.provider, model: m.model, baseUrl: m.baseUrl, separateKey: m.separateKey, keySet: m.keySet };
    }), v.models.default, v.models.backups);
    defaultId = v.models.default;
    backupOn = {};
    v.models.backups.forEach(function (id) { backupOn[id] = true; });
    renderModels();
    var rv = v.review;
    fillSelect($("rv-task"), ["Auto"].concat(rv.taskOrder).map(function (x) { return { value: x, label: x }; }), rv.defaultTask);
    $("rv-count").min = String(rv.minBar);
    $("rv-count").max = String(rv.maxBar);
    $("rv-count").value = String(rv.barCount);
    order = rv.taskOrder.slice();
    renderOrder();
    $("rv-highlight").checked = rv.highlightChanges;
    $("rv-remember").checked = rv.rememberLastChoice;
    $("rv-prefetch").checked = rv.prefetchOtherModes;
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
      model: {
        saved: models.map(function (m) {
          return { id: m.id, provider: m.provider, model: m.model, baseUrl: providerOf(m.provider).urlEditable ? m.baseUrl : "", separateKey: m.separateKey };
        }),
        default: defaultId,
        backups: backupList()
      },
      review: {
        taskOrder: order.slice(),
        barCount: readCount(),
        defaultTask: $("rv-task").value,
        highlightChanges: $("rv-highlight").checked,
        rememberLastChoice: $("rv-remember").checked,
        prefetchOtherModes: $("rv-prefetch").checked
      },
      keys: { set: typed, remove: Object.keys(removed) },
      shortcuts: { optimize: chords.optimize, optimizeSelection: chords.optimizeSelection, voice: chords.voice },
      voice: readVoice(),
      requirements: reqs
    };
  }

  function problem(draft) {
    if (draft.model.saved.some(function (m) { return m.model === ""; })) return "Enter a name for every saved model.";
    if (models.some(function (m) { return m.separateKey && !m.keySet && !typed[keyIdOf(m)]; })) return "A model with its own key needs that key, or switch it back to the provider key.";
    if (draft.review.barCount < view.review.minBar || draft.review.barCount > view.review.maxBar) return "The task bar can show " + view.review.minBar + " to " + view.review.maxBar + " tasks.";
    if (draft.voice.enabled && draft.voice.model === "") return "Enter a model name for voice, or turn voice off.";
    if (draft.voice.maxSeconds < 10 || draft.voice.maxSeconds > 600) return "Voice can stop after 10 to 600 seconds.";
    if (view && Object.keys(draft.requirements).some(function (id) { return draft.requirements[id].trim().length > view.requirementsMax; })) return "A requirements text is too long.";
    return "";
  }

  function update() {
    syncKeys();
    syncModels();
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

  $("m-add").addEventListener("click", function () {
    if (!view || models.length >= view.models.max) return;
    var p = providerOf(models[0] ? models[0].provider : "groq");
    models.push({ id: newId(), provider: p.id, model: p.defaultFallbackModel || p.defaultModel, baseUrl: "", separateKey: false, keySet: false });
    refreshModels();
  });
  $("rv-count").addEventListener("input", renderOrder);
  $("r-task").addEventListener("change", showRequirement);
  $("r-text").addEventListener("input", function () { reqs[$("r-task").value] = $("r-text").value; });
  // Leaving the page or the window must put the app's shortcuts back.
  window.addEventListener("blur", stopRecording);
  document.querySelectorAll("[data-nav]").forEach(function (button) { button.addEventListener("click", stopRecording); });
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
