// Quality check page: pick the judge and a test set, see what a run costs, start it with a click, watch it, cancel it.
// The app does the work and sends back counts only. No text from the app goes in except with textContent.
(function () {
  "use strict";

  var $ = function (id) { return document.getElementById(id); };
  var info = null, running = false, currentRun = null, hasCases = false, planned = null, planTicket = 0;
  var ERROR_TEXT = {
    bad_request: "Those choices can't be used. Check them and try again.",
    busy: "A quality check is already running.",
    not_ready: "Choose a test set first, and make sure your main model has a key (Settings page).",
    rate_limited: "Too many requests. Wait a moment and try again.",
    timeout: "The app did not answer in time. Try again.",
    failed: "That did not work. Nothing was changed."
  };

  function say(id, text, tone) {
    $(id).textContent = text;
    $(id).setAttribute("data-tone", tone || "");
  }

  function options() {
    return { repeat: Number($("q-repeat").value), judge: $("q-judge").checked, heldOut: $("q-held").checked };
  }

  function sync() {
    var lock = running;
    ["q-provider", "q-model", "q-choose", "q-repeat", "q-judge", "q-held", "q-judge-save"].forEach(function (id) { $(id).disabled = lock; });
    $("q-judge-save").disabled = lock || !$("q-model").value.trim() || !judgeChanged();
    $("q-start").disabled = lock || !hasCases || !planned;
    $("q-start").hidden = lock;
    $("q-cancel").hidden = !lock;
    $("q-bar").hidden = !lock;
  }

  function judgeChanged() {
    return !info || $("q-provider").value !== info.judgeProvider || $("q-model").value.trim() !== info.judgeModel;
  }

  function render(view) {
    info = view;
    running = view.running;
    hasCases = !!view.cases;
    $("q-provider").value = view.judgeProvider;
    $("q-model").value = view.judgeModel;
    var note = !view.judgeKeySet ? "No key saved for this provider. Without one, your main model's fallback grades instead, and the report says it is not independent."
      : view.judgeSameAsMain ? "Same provider as your main model, so it grades its own family. Results are less independent."
      : "";
    $("q-judge-note").textContent = note;
    $("q-cases").textContent = view.cases
      ? view.cases.name + ": " + view.cases.cases + " cases, " + view.cases.heldOut + " of them held out."
      : "None chosen. Pick the repo's eval/cases.json.";
    $("q-open").hidden = !view.hasResults;
    sync();
    refreshPlan();
  }

  // The cost comes from the app, which knows each case's minimum runs. It shows before the Start button can be used.
  function refreshPlan() {
    planned = null;
    var ticket = ++planTicket;
    if (!hasCases) { say("q-cost", "Choose a test set to see what a run would cost."); sync(); return; }
    window.phrasor.qualityPlan(options()).then(function (plan) {
      if (ticket !== planTicket) return;
      planned = plan;
      var text = "This run makes about " + plan.calls + " requests (" + plan.runs + " rewrites" + (options().judge ? " and the same number of gradings" : "") +
        ") and takes about " + plan.minutes + " minutes. Free plans allow roughly " + plan.freeDailyCalls + " requests a day per model.";
      if (plan.overAllowance) text += " That is more than one day's free allowance: the run will stop when the limit is reached and keep what finished.";
      say("q-cost", text, plan.overAllowance ? "warn" : "");
      $("q-start").textContent = "Start quality check (about " + plan.calls + " requests)";
      sync();
    }, function (error) {
      if (ticket !== planTicket) return;
      say("q-cost", ERROR_TEXT[error.message] || "The cost could not be worked out.", "bad");
      sync();
    });
  }

  function load() {
    window.phrasor.qualityGet().then(render, function () { say("q-state", "The quality check could not be read.", "bad"); });
  }

  $("q-provider").addEventListener("change", function () {
    // Switching provider: swap in that provider's default model unless the box holds something the user typed.
    if (info && info.defaultModels) {
      var typed = $("q-model").value.trim();
      var known = Object.keys(info.defaultModels).map(function (k) { return info.defaultModels[k]; });
      if (!typed || known.indexOf(typed) >= 0) $("q-model").value = info.defaultModels[$("q-provider").value];
    }
    say("q-judge-state", "");
    sync();
  });
  $("q-model").addEventListener("input", function () { say("q-judge-state", ""); sync(); });

  $("q-judge-save").addEventListener("click", function () {
    window.phrasor.qualitySetJudge($("q-provider").value, $("q-model").value.trim()).then(function () {
      say("q-judge-state", "Saved.", "ok");
      load();
    }, function (error) { say("q-judge-state", ERROR_TEXT[error.message] || "Could not save.", "bad"); });
  });

  $("q-choose").addEventListener("click", function () {
    window.phrasor.qualityChooseCases().then(function (reply) {
      if (reply && reply.cases) load(); // cancelled in the dialog: nothing to do
    }, function (error) { say("q-state", error.message === "not_ready" ? "That file is not a test set." : (ERROR_TEXT[error.message] || "The test set could not be read."), "bad"); });
  });

  ["q-repeat", "q-judge", "q-held"].forEach(function (id) { $(id).addEventListener("change", refreshPlan); });

  // Starting always takes this click, after the cost above has been shown. Nothing starts on its own.
  $("q-start").addEventListener("click", function () {
    if (!planned) return;
    running = true;
    say("q-state", "Starting…");
    sync();
    window.phrasor.qualityRun(options()).then(function (reply) {
      currentRun = reply.run;
      $("q-bar").value = 0;
      $("q-bar").max = Math.max(1, planned.runs);
    }, function (error) {
      running = false;
      say("q-state", ERROR_TEXT[error.message] || "The quality check could not start.", "bad");
      sync();
    });
  });

  $("q-cancel").addEventListener("click", function () {
    $("q-cancel").disabled = true;
    say("q-state", "Cancelling…");
    window.phrasor.qualityCancel().catch(function () { $("q-cancel").disabled = false; });
  });

  $("q-open").addEventListener("click", function () {
    window.phrasor.qualityOpenResults().then(function (reply) {
      if (!reply.opened) say("q-state", "There is no results folder to open.", "warn");
    }, function () {});
  });

  var END_TEXT = {
    done: function (e) { return "Finished: " + e.passed + " of " + e.total + " runs passed, " + e.errored + " errored. The report is in the results folder."; },
    stopped: function (e) { return "Stopped early: the provider's limit was reached. The " + e.total + " runs that finished are saved. Run the rest on another day."; },
    cancelled: function () { return "Cancelled. A cancelled check saves nothing."; },
    failed: function () { return "The quality check failed. Check your main model's key and the test set, then try again."; }
  };

  window.phrasor.onQuality(function (event) {
    if (currentRun !== null && event.run !== currentRun) return; // an event for an older run
    if (event.state === "running") {
      running = true;
      $("q-bar").max = Math.max(1, event.total);
      $("q-bar").value = event.current;
      say("q-state", event.waiting ? "Waiting for the provider's rate limit… (run " + event.current + " of " + event.total + ")"
        : "Run " + event.current + " of " + event.total + ". This can take a long time. You can leave this page.");
      sync();
      return;
    }
    running = false;
    currentRun = null;
    var text = END_TEXT[event.state];
    say("q-state", text ? text(event) : "The quality check ended.", event.state === "done" && event.errored === 0 ? "ok" : event.state === "failed" ? "bad" : "warn");
    $("q-cancel").disabled = false;
    load();
  });

  window.addEventListener("phrasor:page", function (event) { if (event.detail === "quality") load(); });
  load();
})();
