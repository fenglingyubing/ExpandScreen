(() => {
  const tabButtons = Array.from(document.querySelectorAll("[data-tab]"));
  const panes = Array.from(document.querySelectorAll("[data-pane]"));

  function setActive(tabName) {
    for (const button of tabButtons) {
      const isActive = button.dataset.tab === tabName;
      button.classList.toggle("is-active", isActive);
      button.setAttribute("aria-selected", isActive ? "true" : "false");
    }

    for (const pane of panes) {
      pane.classList.toggle("is-active", pane.dataset.pane === tabName);
    }
  }

  for (const button of tabButtons) {
    button.addEventListener("click", () => setActive(button.dataset.tab));
  }
})();

