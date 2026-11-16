// Thin renderer: shows one page at a time, greets by the hour, sends the theme choice to the app, shows Home status
// and runs Try-it. All text from the app goes in with textContent. No data and no rules live here; the window buttons are painted by the app itself, so nothing here touches them.
(function () {
  "use strict";

  var pages = document.querySelectorAll("[data-page]");
  var navButtons = document.querySelectorAll("[data-nav]");
  var themeRadios = document.querySelectorAll("input[name=theme]");

  function show(name, moveFocus) {
    var shown = null;
    pages.forEach(function (page) {
      var on = page.getAttribute("data-page") === name;
      page.hidden = !on;
      if (on) shown = page;
    });
    if (!shown) return;
    // Only the rail buttons get aria-current; links inside pages that point at a page share data-nav.
    document.querySelectorAll(".rail [data-nav]").forEach(function (button) {
      if (button.getAttribute("data-nav") === name) button.setAttribute("aria-current", "page");
      else button.removeAttribute("aria-current");
    });
    // Move focus to the page's heading so keyboard and screen-reader users land on the new page.
    var heading = shown.querySelector("h1");
    if (heading && moveFocus) {
      heading.setAttribute("tabindex", "-1");
      heading.focus();
    }
    document.getElementById("main").scrollTop = 0;
  }

  navButtons.forEach(function (button) {
    button.addEventListener("click", function () { show(button.getAttribute("data-nav"), true); });
  });

  document.querySelectorAll("[data-focus]").forEach(function (button) {
    button.addEventListener("click", function () {
      var target = document.getElementById(button.getAttribute("data-focus"));
      if (target) target.focus();
    });
  });

  var hour = new Date().getHours();
  document.getElementById("greet").textContent = hour < 12 ? "Good morning." : hour < 18 ? "Good afternoon." : "Good evening.";

  function showTheme(mode) {
    themeRadios.forEach(function (radio) { radio.checked = radio.value === mode; });
  }

  themeRadios.forEach(function (radio) {
    radio.addEventListener("change", function () {
      window.phrasor.setTheme(radio.value).then(
        function (reply) { showTheme(reply.theme); },
        syncTheme);
    });
  });

  function syncTheme() {
    window.phrasor.getState().then(function (state) { showTheme(state.theme); }, function () {});
  }

  // ---- Home status ----
  var $ = function (id) { return document.getElementById(id); };
  var STATE_TEXT = {
    ready: ["Running. Shortcuts are on.", "ok"],
    paused: ["Paused. Shortcuts are off.", "warn"],
    needskey: ["Running, but no API key is set.", "warn"],
    shortcutproblem: ["Running, but a shortcut could not be registered.", "bad"]
  };
  var paused = false;
  var modeTouched = false;

  function setText(id, text, tone) {
    var el = $(id);
    el.textContent = text;
    if (tone !== undefined) el.setAttribute("data-tone", tone);
  }

  function chordNodes(chord) {
    var holder = document.createDocumentFragment();
    chord.split("+").forEach(function (part, i) {
      if (i > 0) {
        var plus = document.createElement("span");
        plus.setAttribute("aria-hidden", "true");
        plus.textContent = "+";
        holder.appendChild(plus);
      }
      var key = document.createElement("kbd");
      key.className = "kbd";
      key.textContent = part;
      holder.appendChild(key);
    });
    return holder;
  }

  function renderShortcuts(h) {
    var main = h.shortcuts.filter(function (s) { return s.id === "optimize"; })[0];
    var chord = $("chord");
    chord.replaceChildren();
    if (main) {
      chord.appendChild(chordNodes(main.chord));
      chord.setAttribute("aria-label", main.chord.split("+").join(" plus "));
    }
    $("hsub").textContent = main && !main.registered && !h.paused
      ? "This shortcut is not active. Another app may be using it."
      : "Select text in any app, then press";
    var alt = $("alt");
    alt.replaceChildren();
    h.shortcuts.filter(function (s) { return s !== main; }).forEach(function (s) {
      var item = document.createElement("span");
      item.className = "i";
      item.appendChild(document.createTextNode(s.label + " "));
      var key = document.createElement("kbd");
      key.className = "kbd";
      key.textContent = s.chord;
      item.appendChild(key);
      if (!s.registered && !h.paused) item.appendChild(document.createTextNode(" (not active)"));
      alt.appendChild(item);
    });
  }

  function renderHome(h) {
    paused = h.paused;
    var state = STATE_TEXT[h.state] || STATE_TEXT.ready;
    setText("s-state", state[0], state[1]);
    setText("s-model", h.provider + " · " + h.model);
    if (!h.keyRequired) setText("s-key", "Not needed for this provider.", "");
    else if (h.keySet) setText("s-key", "Saved.", "ok");
    else setText("s-key", "Not set. Add it in Settings (tray menu).", "warn");
    $("pause").textContent = h.paused ? "Resume shortcuts" : "Pause shortcuts";
    renderShortcuts(h);
    if (!modeTouched) $("mode").value = h.defaultMode;
  }

  function refreshHome() {
    window.phrasor.home().then(renderHome, function () {
      setText("s-state", "Status is not available right now.", "warn");
    });
  }

  $("pause").addEventListener("click", function () {
    window.phrasor.setPaused(!paused).then(refreshHome, refreshHome);
  });
  $("mode").addEventListener("change", function () { modeTouched = true; });
  window.addEventListener("focus", refreshHome); // the tray's Pause may have changed while the window was behind

  // ---- Try-it: the rewrite is shown here only. Nothing is pasted, saved or sent to another app. ----
  var draft = $("draft"), runBtn = $("run"), cancelBtn = $("cancel"), copyBtn = $("copy");
  var running = false, currentRun = null, copyTimer = 0;
  var IDLE_NOTE = $("trystate").textContent;
  var ERROR_TEXT = {
    bad_request: "That draft can't be sent. Check it and try again.",
    too_large: "That draft is too long to rewrite here.",
    busy: "A rewrite is already running.",
    rate_limited: "Too many requests. Wait a moment and try again."
  };

  function syncRunButton() { runBtn.disabled = running || draft.value.trim().length === 0; }

  function setRunning(on) {
    running = on;
    draft.readOnly = on;
    cancelBtn.hidden = !on;
    cancelBtn.disabled = false;
    syncRunButton();
    $("trystate").textContent = on ? "Rewriting…" : IDLE_NOTE;
  }

  function showError(text) {
    var el = $("tryerr");
    el.textContent = text;
    el.hidden = false;
  }

  function clearOutput() {
    $("tryerr").hidden = true;
    $("result").hidden = true;
    $("out").textContent = "";
    $("notes").replaceChildren();
    copyBtn.textContent = "Copy";
  }

  function addNote(text) {
    var li = document.createElement("li");
    li.textContent = text;
    $("notes").appendChild(li);
  }

  function showResult(p) {
    $("out").textContent = p.rewrite;
    if (p.verdict === "Unchanged") addNote("No changes were needed.");
    p.changes.forEach(addNote);
    p.warnings.forEach(function (w) { addNote("Check: " + w); });
    $("result").hidden = false;
  }

  draft.addEventListener("input", syncRunButton);

  runBtn.addEventListener("click", function () {
    clearOutput();
    setRunning(true);
    window.phrasor.tryItRun(draft.value, $("task").value, $("mode").value).then(
      function (reply) { currentRun = reply.run; },
      function (error) {
        setRunning(false);
        showError(ERROR_TEXT[error.message] || "Something went wrong. Try again.");
      });
  });

  cancelBtn.addEventListener("click", function () {
    cancelBtn.disabled = true;
    $("trystate").textContent = "Cancelling…";
    window.phrasor.tryItCancel().catch(function () {});
  });

  window.phrasor.onTryIt(function (event) {
    if (event.run !== currentRun) return; // a result from an older run
    setRunning(false);
    if (event.ok) showResult(event.payload);
    else if (event.error === "cancelled") $("trystate").textContent = "Cancelled.";
    else showError(event.message);
  });

  copyBtn.addEventListener("click", function () {
    window.phrasor.copyRewrite().then(function (reply) {
      copyBtn.textContent = reply.copied ? "Copied" : "Nothing to copy";
    }, function () { copyBtn.textContent = "Couldn't copy"; });
    clearTimeout(copyTimer);
    copyTimer = setTimeout(function () { copyBtn.textContent = "Copy"; }, 2500);
  });

  show("home");
  syncTheme();
  refreshHome();
})();
