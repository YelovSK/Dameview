# Architecture

Dameview is a Windows desktop application distributed as one Native AOT executable. It uses Win32 for the window and message loop, WIC for image decoding, and Direct2D for a custom-drawn UI.

Production code is split between `Dameview.Core`, which contains headless application state and logic, and the Windows `Dameview` executable, which contains the frontend and native implementations. The executable references Core, never the reverse, so the compiler keeps Core independent of presentation and Windows-specific code. Folders within each project are responsibility boundaries rather than independently deployable layers.

## Runtime shape

`Program` selects between update, installation, and normal viewer startup. In viewer mode, `DameviewApp` is the composition root: it creates the window, renderer, workspace, shared services, and UI, then connects them with events.

The normal interaction flow is:

1. `AppWindow` translates Win32 messages into application-level input and window events.
2. `DameviewApp` routes those events to the UI or executes a viewer command.
3. The `Viewing` subsystem changes the workspace or active viewing session.
4. Viewing events cause the relevant UI state to be rebound and a frame to be requested.
5. `D2DRenderer` renders the custom UI tree when the window asks for a frame.

Most mutable application and graphics state belongs to the UI thread. File scanning, decoding, thumbnail generation, tile loading, and update work may run in the background, but results are posted back to the UI thread before they affect application state or Direct2D resources.

## Subsystems

### Viewing (`src/Dameview.Core/Viewing`)

`Viewing` owns the logical state of an open workspace independently of its presentation. A workspace is a binary tree of panes and splits. Each pane owns tabs, and each tab owns a viewing session plus the per-tab services used by that session. A session owns navigation state, the accepted image representation, and the viewport.

The workspace is the source of truth for pane layout, active pane, tabs, and active sessions. The UI observes and presents that state; it should not maintain a separate copy of the workspace state.

### Imaging (`src/Dameview.Core/Imaging`, `src/Dameview/Imaging`)

`Imaging` turns a path into a presentation-appropriate image representation. Core contains the representations, policies, and loading contracts used by viewing; the executable contains the shared loading infrastructure and Windows-specific decoders. Together they coordinate foreground loads, previews, preloading, caching, animations, and oversized tiled images.

### Navigation (`src/Dameview.Core/Navigation`)

`Navigation` produces and monitors folder snapshots. A viewing session combines those snapshots with its current selection, but directory enumeration and file-system watching remain separate from workspace and UI concerns.

### UI (`src/Dameview/UI`)

`UI` is a retained tree of custom elements responsible for layout, hit testing, interaction, animation, and drawing. `ViewerUi` is the root of the viewer UI, while workspace views project the logical workspace into pane and split elements.

### Rendering (`src/Dameview/Rendering`)

`Rendering` owns the Direct2D/Direct3D infrastructure and frame lifecycle. UI components own or borrow presentation-side graphics resources as appropriate; logical viewing state and decodable image data live outside the renderer so they can survive graphics-resource recreation.

### Platform (`src/Dameview/Platform`)

`Platform` contains the Win32 boundary: the native window, input translation, synchronization with the message loop, installation, registration, and narrow native helpers.

### Settings (`src/Dameview.Core/Settings`)

`Settings` owns platform-neutral application preferences, their persistence, and live-change delivery. The frontend maps stable setting values such as theme identities and window placement onto UI palettes and native window operations.

### Updates (`src/Dameview/Updates`)

`Updates` owns the check, download, and apply state machine for application updates.

### Commands (`src/Dameview/Commands`)

`Commands` defines the actions exposed by the application and the catalog used to invoke them.

### Serialization (`src/Dameview.Core/Serialization`)

`Serialization` contains the general parsing primitives used by persisted application data.

Tests live in `tests/Dameview.Tests` and broadly mirror the production subsystems.
