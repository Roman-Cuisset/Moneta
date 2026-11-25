# Window placement

Moneta restores window geometry only after checking it against the current monitor work area.

## Behaviour

- The saved position is applied before the first render. A stale negative `Left`/`Top` is moved inside the monitor work area.
- A window larger than the work area is reduced to that work area. A window smaller than the application minimum is grown to the minimum when the monitor permits it.
- Multi-monitor negative coordinates remain valid when they intersect a monitor. Geometry that no longer intersects any monitor is recentred on the primary monitor.
- The application uses physical pixels from `GetWindowRect`/`SetWindowPos` and converts them with the window monitor DPI. The WPF `SystemParameters.WorkArea` value is not used for placement because it is a single DPI-scaled value and does not describe the monitor that owns the window.
- The user can still move or resize the window manually. Off-screen corrections are reapplied only when placement changes or when the window is restored from maximized/minimized.
- The main window saves its normal geometry and maximized state when it closes. Restored geometry is visible through <kbd>Affichage → Plein écran / Quitter le plein écran</kbd> or the normal window controls.

## Secondary windows

Secondary windows are sized by their own content and are centred when they have no saved position. They are not resized to the main window's geometry.

## Verification

`WindowPlacementTests` covers negative coordinates, oversized windows, minimum sizes, dual-monitor coordinates, persistence settings, and common work areas for 1280×720, 1366×768, 1920×1080, and 2560×1440 at 100–200% scaling. The live check used the current 1920×1080 display at 125% and a saved 1024×600 DIP window.
