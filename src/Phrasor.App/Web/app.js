// Thin renderer: shows one page at a time, greets by the hour and sends the theme choice to the app.
// No data and no logic live here; the window buttons are painted by the app itself, so nothing here touches them.
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

  show("home");
  syncTheme();
})();
