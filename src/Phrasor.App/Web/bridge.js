// The page's only way to ask the app for anything: window.phrasor.<command>(...). Nothing else is exposed.
// Each call posts one JSON string to the app and resolves with the reply's payload, or rejects with an error kind.
// The commands and their shapes are written down in contracts/*.json and checked by the Core tests.
(function () {
  "use strict";

  var host = window.chrome && window.chrome.webview;
  var pending = {};
  var counter = 0;
  var tryItHandler = null;
  var settingsTestHandler = null;
  var TIMEOUT_MS = 10000;

  if (host) {
    host.addEventListener("message", function (event) {
      var reply;
      try { reply = JSON.parse(event.data); } catch (e) { return; }
      // Pushed by the app, not a reply: the finished (or failed) Try-it run.
      if (reply && reply.event === "tryit") { if (tryItHandler) tryItHandler(reply); return; }
      if (reply && reply.event === "settingsTest") { if (settingsTestHandler) settingsTestHandler(reply); return; }
      var call = reply && typeof reply.id === "string" ? pending[reply.id] : null;
      if (!call) return;
      delete pending[reply.id];
      clearTimeout(call.timer);
      if (reply.ok) call.resolve(reply.payload); else call.reject(new Error(String(reply.error)));
    });
  }

  function send(type, payload) {
    return new Promise(function (resolve, reject) {
      if (!host) { reject(new Error("no_host")); return; }
      var id = "c" + (++counter);
      var message = { id: id, type: type };
      if (payload !== undefined) message.payload = payload;
      pending[id] = {
        resolve: resolve,
        reject: reject,
        timer: setTimeout(function () { delete pending[id]; reject(new Error("timeout")); }, TIMEOUT_MS)
      };
      host.postMessage(JSON.stringify(message));
    });
  }

  window.phrasor = Object.freeze({
    getState: function () { return send("state.get"); },
    setTheme: function (mode) { return send("theme.set", { mode: mode }); },
    minimize: function () { return send("window.minimize"); },
    maximize: function () { return send("window.maximize"); },
    close: function () { return send("window.close"); },
    openLink: function (id) { return send("link.open", { id: id }); },
    home: function () { return send("home.get"); },
    setPaused: function (paused) { return send("pause.set", { paused: paused }); },
    tryItRun: function (text, task, mode) { return send("tryit.run", { text: text, task: task, mode: mode }); },
    tryItCancel: function () { return send("tryit.cancel"); },
    copyRewrite: function () { return send("clipboard.copy"); },
    onTryIt: function (handler) { tryItHandler = handler; },
    settingsGet: function () { return send("settings.get"); },
    settingsSave: function (draft) { return send("settings.save", draft); },
    settingsTest: function (draft) { return send("settings.testConnection", draft); },
    onSettingsTest: function (handler) { settingsTestHandler = handler; },
    shortcutRecordStart: function () { return send("shortcut.recordStart"); },
    shortcutRecordStop: function () { return send("shortcut.recordStop"); }
  });
})();
