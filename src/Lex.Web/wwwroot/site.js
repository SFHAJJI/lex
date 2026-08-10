// The proof navigation is a native disclosure first. This small enhancement adds the desktop
// hover path and consistent dismissal without turning a list of links into an application menu.
document.querySelectorAll("details.proofnav").forEach((details) => {
  const summary = details.querySelector("summary");
  if (!summary) return;

  let closeTimer;
  const expanded = () => summary.setAttribute("aria-expanded", String(details.open));
  const close = (returnFocus = false) => {
    clearTimeout(closeTimer);
    details.open = false;
    if (returnFocus) summary.focus();
  };
  const closeSoon = () => {
    clearTimeout(closeTimer);
    closeTimer = setTimeout(() => {
      if (!details.matches(":hover") && !details.contains(document.activeElement)) close();
    }, 180);
  };

  details.addEventListener("toggle", expanded);
  summary.addEventListener("click", (event) => {
    // Pointer entry may have opened the native disclosure immediately before the click. In
    // that case the user's first click means "keep this open", not "close what hover opened".
    if (details.dataset.hoverOpened !== "true") return;
    event.preventDefault();
    delete details.dataset.hoverOpened;
  });
  details.addEventListener("keydown", (event) => {
    if (event.key !== "Escape" || !details.open) return;
    event.preventDefault();
    close(true);
  });
  details.addEventListener("focusout", closeSoon);

  if (matchMedia("(hover: hover) and (pointer: fine)").matches) {
    details.addEventListener("pointerenter", () => {
      clearTimeout(closeTimer);
      if (!details.open) {
        details.dataset.hoverOpened = "true";
        details.open = true;
      }
    });
    details.addEventListener("pointerleave", closeSoon);
  }

  document.addEventListener("pointerdown", (event) => {
    if (details.open && !details.contains(event.target)) close();
  });
  expanded();
});
