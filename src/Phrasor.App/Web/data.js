// Settings page, Data group: open the folders, and delete everything after a confirmation shown here.
// These buttons act at once; they are not part of the Save and Discard draft. No text from the user passes through here.
(function () {
  "use strict";

  var $ = function (id) { return document.getElementById(id); };
  var confirmBox = $("d-confirm"), deleteBtn = $("d-delete"), yes = $("d-yes"), no = $("d-no");

  function say(text, tone) {
    var el = $("d-state");
    el.textContent = text;
    el.setAttribute("data-tone", tone || "");
  }

  function openFolder(which) {
    say("");
    window.phrasor.openFolder(which).catch(function () { say("Couldn't open the folder.", "bad"); });
  }

  function closeConfirm() {
    confirmBox.hidden = true;
    deleteBtn.disabled = false;
    deleteBtn.focus();
  }

  $("d-open-data").addEventListener("click", function () { openFolder("data"); });
  $("d-open-logs").addEventListener("click", function () { openFolder("logs"); });

  deleteBtn.addEventListener("click", function () {
    say("");
    deleteBtn.disabled = true;
    confirmBox.hidden = false;
    no.focus(); // the safe choice is the one the keyboard lands on
  });

  no.addEventListener("click", closeConfirm);
  confirmBox.addEventListener("keydown", function (event) { if (event.key === "Escape") closeConfirm(); });

  yes.addEventListener("click", function () {
    yes.disabled = no.disabled = true;
    window.phrasor.deleteAllData().then(function (reply) {
      if (reply.deleted) {
        say("Deleted. Phrasor is closing.", "ok");
        return;
      }
      yes.disabled = no.disabled = false;
      closeConfirm();
      say("Some files could not be deleted, so Phrasor is still running. Close anything using them and try again.", "bad");
    }, function () {
      yes.disabled = no.disabled = false;
      closeConfirm();
      say("Couldn't delete the data. Nothing was closed.", "bad");
    });
  });
})();
