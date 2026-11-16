// History page and Home's Recent list. The app owns the data: this file only asks for the list, shows it, and sends back
// ids (copy, delete). The only text it sends is the search words. All text from the app goes in with textContent.
(function () {
  "use strict";

  var $ = function (id) { return document.getElementById(id); };
  var NOTICE = {
    moved_aside: "Your saved History could not be read, so it was set aside and started again empty.",
    unavailable: "History is unavailable right now (the file is locked, damaged or from a newer Phrasor). It was left as it is, and nothing can be saved."
  };
  var searchTimer = 0, loadNumber = 0, openId = null;

  function el(tag, className, text) {
    var node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  function button(text, className, onClick) {
    var b = el("button", className || "btn", text);
    b.type = "button";
    b.addEventListener("click", onClick);
    return b;
  }

  function flash(b, text, original) {
    b.textContent = text;
    setTimeout(function () { b.textContent = original; }, 2000);
  }

  // ---- full item (opened on demand: the list itself never carries the text) ----

  function fullPart(title, text) {
    var part = el("div", "hpart");
    part.appendChild(el("h4", "", title));
    var body = el("div", "out", text);
    body.tabIndex = 0;
    part.appendChild(body);
    return part;
  }

  function openItem(item, row, head) {
    var detail = row.querySelector(".hfull");
    if (detail) { // second click closes it
      detail.remove();
      head.setAttribute("aria-expanded", "false");
      openId = null;
      return;
    }
    window.phrasor.historyGet(item.id).then(function (full) {
      var box = el("div", "hfull");
      box.appendChild(fullPart("Your draft", full.original || "(empty)"));
      box.appendChild(fullPart("Rewrite", full.rewrite));
      var tools = el("div", "row");
      var copyRewrite = button("Copy rewrite", "btn", function () {
        window.phrasor.historyCopy(item.id, "rewrite").then(function (r) {
          flash(copyRewrite, r.copied ? "Copied" : "Nothing to copy", "Copy rewrite");
        }, function () { flash(copyRewrite, "Couldn't copy", "Copy rewrite"); });
      });
      var copyDraft = button("Copy draft", "btn", function () {
        window.phrasor.historyCopy(item.id, "original").then(function (r) {
          flash(copyDraft, r.copied ? "Copied" : "Nothing to copy", "Copy draft");
        }, function () { flash(copyDraft, "Couldn't copy", "Copy draft"); });
      });
      var del = button("Delete", "btn danger", function () {
        del.disabled = true;
        window.phrasor.historyDelete(item.id).then(function () {
          changed();
        }, function () { del.disabled = false; flash(del, "Couldn't delete", "Delete"); });
      });
      tools.appendChild(copyRewrite);
      tools.appendChild(copyDraft);
      tools.appendChild(del);
      box.appendChild(tools);
      row.appendChild(box);
      head.setAttribute("aria-expanded", "true");
      openId = item.id;
    }, function () { flash(head.querySelector("small"), "Couldn't open this item", head.querySelector("small").textContent); });
  }

  // ---- the page ----

  function renderGroups(view) {
    var holder = $("h-groups");
    holder.replaceChildren();
    view.groups.forEach(function (group) {
      var sec = el("section", "sec");
      var title = el("h2", "", group.label);
      title.className = "hday";
      sec.appendChild(title);
      var grp = el("div", "grp");
      group.items.forEach(function (item) {
        var row = el("div", "sr col");
        var head = el("button", "hhead");
        head.type = "button";
        head.setAttribute("aria-expanded", "false");
        head.appendChild(el("span", "hprev", item.preview));
        head.appendChild(el("small", "", item.time + " · " + item.rewriteChars.toLocaleString() + " characters"));
        head.addEventListener("click", function () { openItem(item, row, head); });
        row.appendChild(head);
        grp.appendChild(row);
        if (item.id === openId) openItem(item, row, head);
      });
      sec.appendChild(grp);
      holder.appendChild(sec);
    });
  }

  function renderPage(view) {
    var notice = $("h-notice");
    notice.hidden = !view.notice;
    notice.textContent = view.notice ? NOTICE[view.notice] || "" : "";
    $("h-retention").value = String(view.retentionDays);
    renderGroups(view);
    var searching = $("h-search").value.trim().length > 0;
    var none = view.matches === 0;
    $("h-empty").hidden = !none;
    $("h-empty-b").textContent = searching ? "No matches" : "Nothing saved yet";
    $("h-empty-s").textContent = searching
      ? "Try fewer or different words."
      : "Press Save in Try it or in the review window, and the rewrite is listed here. Nothing is stored unless you press Save.";
    $("h-count").textContent = view.total === 0 ? "" : searching
      ? view.matches + " of " + view.total + " saved"
      : view.total + " saved";
    $("h-delete-all").disabled = view.total === 0;
  }

  function loadPage() {
    var number = ++loadNumber;
    window.phrasor.historyList($("h-search").value.trim().slice(0, 200), 0).then(function (view) {
      if (number === loadNumber) renderPage(view);
    }, function () {
      if (number !== loadNumber) return;
      $("h-notice").hidden = false;
      $("h-notice").textContent = NOTICE.unavailable;
    });
  }

  // ---- Recent on Home ----

  function loadRecent() {
    window.phrasor.historyList("", 3).then(function (view) {
      var list = $("recent-list");
      list.replaceChildren();
      var rows = [];
      view.groups.forEach(function (g) { g.items.forEach(function (item) { rows.push({ label: g.label, item: item }); }); });
      list.hidden = rows.length === 0;
      $("recent-empty").hidden = rows.length > 0;
      rows.forEach(function (r) {
        var row = el("div", "sr");
        var text = el("span", "st", r.item.preview);
        text.appendChild(el("small", "", r.label + " · " + r.item.time));
        row.appendChild(text);
        var copy = button("Copy", "btn", function () {
          window.phrasor.historyCopy(r.item.id, "rewrite").then(function (reply) {
            flash(copy, reply.copied ? "Copied" : "Nothing to copy", "Copy");
          }, function () { flash(copy, "Couldn't copy", "Copy"); });
        });
        row.appendChild(copy);
        list.appendChild(row);
      });
    }, function () { /* Home keeps its "Nothing saved yet" note */ });
  }

  function changed() {
    loadRecent();
    if (!$("h-groups").closest("[data-page]").hidden) loadPage();
  }

  // ---- controls ----

  $("h-search").addEventListener("input", function () {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(loadPage, 200);
  });

  $("h-retention").addEventListener("change", function () {
    var select = $("h-retention");
    window.phrasor.historySetRetention(Number(select.value)).then(loadPage, loadPage);
  });

  var confirmBox = $("h-confirm"), deleteAll = $("h-delete-all");
  function closeConfirm() {
    confirmBox.hidden = true;
    deleteAll.hidden = false;
    deleteAll.focus();
  }
  deleteAll.addEventListener("click", function () {
    deleteAll.hidden = true;
    confirmBox.hidden = false;
    $("h-no").focus(); // the safe choice is the one the keyboard lands on
  });
  $("h-no").addEventListener("click", closeConfirm);
  confirmBox.addEventListener("keydown", function (event) { if (event.key === "Escape") closeConfirm(); });
  $("h-yes").addEventListener("click", function () {
    window.phrasor.historyDeleteAll().then(function () {
      closeConfirm();
      changed();
    }, function () {
      closeConfirm();
      $("h-notice").hidden = false;
      $("h-notice").textContent = "Couldn't delete the saved History.";
    });
  });

  window.addEventListener("phrasor:page", function (event) {
    if (event.detail === "history") loadPage();
    if (event.detail === "home") loadRecent();
  });
  window.addEventListener("phrasor:history-changed", changed);
  loadRecent();
})();
