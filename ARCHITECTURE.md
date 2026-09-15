# Architecture

Dameview is a Windows image viewer distributed as a single Native AOT executable. It uses Win32 for the window and message loop, WIC for image decoding, and Direct2D for rendering its custom UI.

## Project boundaries

Production code is split into three projects:

- `Dameview.Core` contains application state and logic that does not depend on Windows, rendering, or UI code.
- `Dameview.Win32` contains low-level Windows mechanisms such as windowing, input translation, COM-initialized worker queues, the WinHTTP transport, and shell integration. These mechanisms do not depend on Core or the executable's application model.
- `Dameview` is the executable. It contains the frontend and application-specific workflows, and references both libraries.

The project graph is deliberately one-way:

```text
Dameview -> Dameview.Core
Dameview -> Dameview.Win32
```

`Dameview.Win32` is not intended to contain every Windows-specific implementation. WIC decoding and Direct2D rendering remain in the executable because they implement Dameview's imaging and presentation workflows.

Within a project, folders and namespaces group related responsibilities; they are not independent layers and do not have a separately enforced dependency graph. Some responsibilities span projects, with platform-neutral state and policy in Core and application-specific implementations in the executable.

## Runtime flow

`Dameview` is a multi-mode executable. `Program` selects viewer, installer/uninstaller, or update-helper mode. In viewer mode, `DameviewApp` is the composition root: it creates the window, renderer, workspace, services, and UI, then connects them.

The main interaction flow is:

1. `AppWindow` translates native window messages into window and input events.
2. `DameviewApp` routes those events to the UI or application commands.
3. Commands and UI actions update the workspace or an active viewing session.
4. State changes update the presentation and request a frame.
5. `D2DRenderer` draws the frame supplied by the UI.

Rendering is demand-driven. Input, state changes, and completed background work request frames. Active animations request subsequent or delayed frames; while nothing changes, the application does not continuously render.

## Ownership

The names below are responsibility areas, generally reflected by folders and namespaces.

`Viewing` owns the workspace, panes, tabs, viewing sessions, and viewport state. The workspace is the source of truth for its layout and active content; the UI presents that state rather than duplicating it.

`Imaging` turns files into image representations suitable for presentation. Core contains the representations and policies used by viewing. The executable contains loading coordination, WIC decoding, animated-image handling, thumbnails, caching, and tiled-image support.

`UI` owns layout, interaction, animation, and drawing. `Rendering` owns the Direct2D and Direct3D infrastructure and frame lifecycle. Logical viewing state and decodable image data live outside both so they survive graphics-resource recreation.

`Commands` defines the application actions exposed by the viewer and maps keyboard shortcuts to those actions. `DameviewApp` executes them against the current workspace, UI, or settings.

`Settings` owns persisted application preferences and delivers changes to the running application. The executable applies those preferences to Core state, UI presentation, and native window behavior.

`Updates` owns release discovery, download state, and update handoff. `Installation` owns Dameview's install, uninstall, registration, and relaunch workflows. Both use native mechanisms from `Dameview.Win32` while keeping application policy in the executable.

## Threading

Most mutable application state and all Direct2D resources belong to the window thread. File scanning, image decoding, thumbnail loading, tile production, and update work run in the background. Results return to the window thread before changing application or presentation state. Work that uses COM runs on COM-initialized workers.

## Workspace model

`ViewerWorkspace` represents the pane layout as a binary tree. `ViewerPane` objects are its leaves, and `WorkspaceSplit` objects divide the available area between two child nodes. Each pane owns a non-empty group of tabs and its active-tab selection; each `ViewerTab` owns one `ViewerSession`.

For example, a workspace with three panes may appear as follows. Square brackets mark the active tab in each pane.

```text
+---------------------------+---------------------------+
| Pane A                    | Pane B                    |
| Tabs: cat.jpg | [dog.jpg] | Tabs: [map.png]           |
| Session: dog.jpg          | Session: map.png          |
+---------------------------+---------------------------+
| Pane C                                                |
| Tabs: [diagram.png]                                   |
| Session: diagram.png                                  |
+-------------------------------------------------------+
```

The upper area is split into panes A and B, and that combined area is split from pane C. More complex layouts are formed by nesting the same two-way split.

Workspace operations mutate this model directly. `WorkspaceView` mirrors the tree for presentation, retaining views for panes that survive a layout change, but it is not a second source of workspace state.

## Custom UI

The UI is a retained tree rooted at `ViewerUi` and managed by `UiRoot`. Each `UiElement` owns its children, desired size, arranged bounds, visual state, and element-specific interaction and drawing behavior.

Layout uses a measure-and-arrange pass in device-independent pixels. `UiRoot` reruns layout when the surface size or DPI changes, or when an element invalidates layout.

`AppWindow` reports pointer positions in physical pixels. `UiRoot` converts them to device-independent pixels, hit-tests the tree from front to back, and bubbles pointer events from the target through its ancestors. The root also owns hover, keyboard focus, pointer capture, cursor selection, focus navigation, and routing of key and text input.

`ViewerUi.Update` advances animations and reports whether another frame is needed. During rendering, `UiDrawContext` walks the tree recursively and applies each element's position, clip, and inherited opacity before drawing it with Direct2D. `D2DRenderer` owns the graphics device, swap chain, and frame lifecycle. UI objects may retain element-specific DirectWrite and Direct2D resources that are expensive to recreate. `UiDrawContext` is frame-local and carries borrowed drawing state through the tree.

## Image representations and caching

Image loading selects a representation according to the source. Ordinary static images use decoded pixel data for Direct2D upload, animated images use an animation session, and images that exceed normal bitmap limits use a tiled source. A viewing session owns its accepted representation and disposes it when it is replaced or the session closes; presentation code borrows it.

The caches serve different stages of this pipeline. `ThumbnailCoordinator` decodes thumbnails on background workers and deduplicates concurrent requests. `ThumbnailImageLoader` uploads them into a UI-thread GPU cache and hands out leases. `PresentationImageLoader` is the matching facade for full images, and also joins a GPU thumbnail with the source dimensions to produce the blurred preview. `RenderBitmapCache` is reused for two UI-thread-owned GPU caches: one for uploaded full-image bitmaps and one for uploaded thumbnails. Displayed images and visible thumbnails hold leases that prevent their entries from being evicted. Each `ImagePanel` caches a pre-scaled Direct2D bitmap for its current static viewport, avoiding repeated high-quality scaling while the source and viewport remain unchanged. Tiled presentation owns a bounded set of GPU tiles rather than constructing one full-size bitmap.

## Installation and updates

`InstallerApp` uses the same window, renderer, and custom UI infrastructure as the viewer, while `AppInstallation` owns the install and uninstall workflow.

Installation is per-user. It copies the executable to its installed location and creates the shortcut, installed-program entry, and image-viewer registration through mechanisms provided by `Dameview.Win32`. Running a portable copy does not create or modify that installed state unless installation is requested.

`UpdateService` checks releases and downloads an update while the viewer is running. Applying it launches a temporary copy of the current executable and closes the viewer. The helper waits for the old process, delegates replacement and registration to `AppInstallation`, relaunches the installed copy, and schedules its temporary files for deletion.
