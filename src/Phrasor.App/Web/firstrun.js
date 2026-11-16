// First run: three steps shown while no usable API key is set (get a key, paste and test it, see the shortcuts).
// The key typed here goes to the app once, in Test and in Save, and is cleared after. No text from the app goes in except with textContent.
(function () {
  "use strict";

  var $ = function (id) { return document.getElementById(id); };
  var skipped = false, testing = false, saving = false, currentTest = null, passed = false;
  var steps = ["fr-1", "fr-2", "fr-3"];
  var ERROR_TEXT = {
    bad_request: "That key can't be used. Check that it is one line with nothing else.",
    busy: "A test is already running. Wait a moment.",
    rate_limited: "Too many requests. Wait a moment and try again.",
    timeout: "The app did not answer in time. Try again."
  };

  function showStep(n, moveFocus) {
    steps.forEach(function (id, i) { $(id).hidden = i !== n - 1; });
    $("fr-step").textContent = "Step " + n + " of 3: " + ["get a free key", "paste and test it", "your shortcuts"][n - 1];
    var heading = $(steps[n - 1]).querySelector("h2");
    if (moveFocus && heading) { heading.setAttribute("tabindex", "-1"); heading.focus(); }
  }

  function open(name) { window.dispatchEvent(new CustomEvent("phrasor:show", { detail: name })); }

  function say(text, tone) {
    $("fr-result").textContent = text;
    $("fr-result").setAttribute("data-tone", tone || "");
  }

  function sync() {
    var has = $("fr-key").value.trim().length > 0;
    $("fr-test").disabled = !has || testing || saving;
    $("fr-continue").disabled = !passed || testing || saving;
  }

  function changed() { passed = false; say(""); sync(); }

  function renderShortcuts(h) {
    var box = $("fr-shortcuts");
    box.replaceChildren();
    h.shortcuts.forEach(function (s) {
      var row = document.createElement("div");
      row.className = "sr";
      var label = document.createElement("span");
      label.className = "st";
      label.textContent = s.label;
      var key = document.createElement("kbd");
      key.className = "kbd";
      key.textContent = s.chord;
      row.appendChild(label);
      row.appendChild(key);
      box.appendChild(row);
    });
  }

  function skip() { skipped = true; $("fr-key").value = ""; open("home"); }

  document.querySelectorAll("[data-fr-link]").forEach(function (button) {
    button.addEventListener("click", function () { window.phrasor.openLink(button.getAttribute("data-fr-link")).catch(function () {}); });
  });
  document.querySelectorAll("[data-fr-skip]").forEach(function (button) { button.addEventListener("click", skip); });
  $("fr-next-1").addEventListener("click", function () { showStep(2, true); $("fr-key").focus(); });
  $("fr-back").addEventListener("click", function () { showStep(1, true); });
  $("fr-key").addEventListener("input", changed);
  $("fr-provider").addEventListener("change", changed);

  $("fr-test").addEventListener("click", function () {
    testing = true;
    passed = false;
    currentTest = null;
    say("Testing…");
    sync();
    window.phrasor.firstRunTest($("fr-provider").value, $("fr-key").value.trim()).then(function (reply) { currentTest = reply.run; }, function (error) {
      testing = false;
      say(ERROR_TEXT[error.message] || "The test could not start. Try again.", "bad");
      sync();
    });
  });

  window.phrasor.onSettingsTest(function (event) {
    if (event.run !== currentTest) return; // a result for another page's test
    testing = false;
    passed = event.ok;
    if (event.ok) say("The key works: connected to " + event.label + " in " + event.ms + " ms.", "ok");
    else say(event.message, "bad");
    sync();
  });

  $("fr-continue").addEventListener("click", function () {
    saving = true;
    sync();
    window.phrasor.firstRunSave($("fr-provider").value, $("fr-key").value.trim()).then(function () {
      saving = false;
      $("fr-key").value = "";
      passed = false;
      say("");
      sync();
      window.dispatchEvent(new Event("phrasor:settings-saved")); // Home shows the new status
      window.phrasor.home().then(renderShortcuts, function () {});
      showStep(3, true);
    }, function (error) {
      saving = false;
      say(ERROR_TEXT[error.message] || "The key could not be saved. Nothing was changed. Try again.", "bad");
      sync();
    });
  });

  $("fr-try").addEventListener("click", function () {
    open("home");
    $("draft").focus();
  });
  $("fr-done").addEventListener("click", function () { open("home"); });

  // Only when the app has no usable key: a saved key (or one that needs none) never sees this.
  window.phrasor.home().then(function (h) {
    if (h.keyRequired && !h.keySet && !skipped) { showStep(1, false); open("firstrun"); }
  }, function () {});
})();
