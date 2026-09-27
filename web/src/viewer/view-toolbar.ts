import type { SceneHelpers } from "./scene-helpers";

// 24x24 line icons; colours come from CSS (currentColor), except the axes' own colours.
const GRID_ICON = `
<svg viewBox="0 0 24 24" aria-hidden="true">
  <rect x="4" y="4" width="16" height="16" rx="1.5" />
  <path d="M4 9.33h16M4 14.67h16M9.33 4v16M14.67 4v16" />
</svg>`;

const AXES_ICON = `
<svg viewBox="0 0 24 24" aria-hidden="true">
  <path class="ax-y" d="M8 16V3M5.5 5.5L8 3l2.5 2.5" />
  <path class="ax-x" d="M8 16h13M18.5 13.5L21 16l-2.5 2.5" />
  <path class="ax-z" d="M8 16l-5 5" />
</svg>`;

/** Two toggle buttons (grid, axes) shown under the navigation gizmo. */
export class ViewToolbar {
  private readonly helpers: SceneHelpers;
  private readonly gridButton: HTMLButtonElement;
  private readonly axesButton: HTMLButtonElement;

  constructor(helpers: SceneHelpers) {
    this.helpers = helpers;

    const bar = document.createElement("div");
    bar.className = "view-toolbar";

    this.gridButton = createToggle(GRID_ICON, "Grid (G)", () => this.toggleGrid());
    this.axesButton = createToggle(AXES_ICON, "Coordinate axes (A)", () => this.toggleAxes());

    bar.append(this.gridButton, this.axesButton);
    document.body.appendChild(bar);

    this.sync();
  }

  toggleGrid(): void {
    this.helpers.gridVisible = !this.helpers.gridVisible;
    this.sync();
  }

  toggleAxes(): void {
    this.helpers.axesVisible = !this.helpers.axesVisible;
    this.sync();
  }

  /** Button look follows the real state (aria-pressed drives the CSS). */
  private sync(): void {
    this.gridButton.setAttribute("aria-pressed", String(this.helpers.gridVisible));
    this.axesButton.setAttribute("aria-pressed", String(this.helpers.axesVisible));
  }
}

function createToggle(icon: string, label: string, onClick: () => void): HTMLButtonElement {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "view-toggle";
  button.innerHTML = icon;
  button.title = label;                      // tooltip on hover
  button.setAttribute("aria-label", label);  // icon-only buttons still need a name
  button.addEventListener("click", onClick);
  return button;
}