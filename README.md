# GrpcViewport

A 3D viewport for desktop .NET applications.
Your app sends geometry (polylines, point clouds, meshes) over **gRPC**; a **Babylon.js** page renders it inside a **WebView2** control and streams its camera back.

- **Batch rendering**: thousands of polylines = one draw call; meshes are merged or GPU-instanced.
- **Handles**: every `AddGeometry` returns a handle; `RemoveGeometry(handle)` removes it again.
- **Camera stream**: subscribe to the viewer's camera (position, target, view/projection matrices, viewport size).
- **Reload-safe**: the server keeps the scene; a reloaded or reconnected page gets a full replay.
- **Blender-style navigation**: grid, world axes, view gizmo, numpad views.
- Works from **.NET 8+** (in-process or gRPC) and from **.NET Framework 4.8** (gRPC-Web, via a sidecar host).

---

## Contents

1. [Architecture](#architecture)
2. [Repository layout](#repository-layout)
3. [Download and run (no build)](#download-and-run-no-build)
4. [Build from source](#build-from-source)
5. [Development workflow](#development-workflow)
6. [Using the viewer](#using-the-viewer)
7. [The API (proto contract)](#the-api-proto-contract)
8. [Client examples](#client-examples)
9. [Ports and endpoints](#ports-and-endpoints)
10. [Releasing](#releasing)
11. [Troubleshooting](#troubleshooting)

---

## Architecture

```
 ┌───────────────────────┐    gRPC-Web (HTTP/1.1)   ┌──────────────────────────────┐
 │ WinForms app (.NET 4.8)│ ───────────────────────► │ GrpcViewport.Host (.NET 8)   │
 │ starts the Host as a  │      127.0.0.1:8080      │  └ GrpcViewport.Server       │
 │ child process         │                          │     Kestrel + SceneHub       │
 └───────────────────────┘                          │     serves the page (viewer/)│
                                                    └──────────────┬───────────────┘
 ┌───────────────────────┐    method calls                         │ gRPC-Web stream
 │ WPF app (.NET 8)      │ ── (in-process, same ──┐                │ (Attach / ReportCamera)
 │  └ GrpcViewport.Server│     SceneHub)          │                ▼
 └───────────────────────┘                        │     ┌──────────────────────────────┐
                                                  └───► │ Babylon.js page in WebView2  │
 any .NET 8+ / Python / … client                        │ builds meshes, reports camera│
 ─── plain gRPC (HTTP/2), 127.0.0.1:50051 ──────────►   └──────────────────────────────┘
```

**Why two ways?**
Kestrel (the ASP.NET Core server) needs .NET 8. A .NET 8 app hosts it **in its own process**.
A .NET Framework 4.8 app cannot, so it starts `GrpcViewport.Host.exe` as a **sidecar** process:

1. WinForms starts `host\GrpcViewport.Host.exe --parent-pid <its PID>`.
2. The host starts Kestrel and prints `READY` on stdout; WinForms waits for that line.
3. WinForms talks to it with gRPC-Web over HTTP/1.1 (.NET Framework has no HTTP/2 client).
4. When WinForms exits, or crashes, the host notices through the parent PID and exits too.

**Inside the server**, `SceneHub` is the single source of truth:

- stores every live geometry request by handle (for replays);
- pushes `Add`/`Remove`/`Clear` commands to every attached page;
- keeps the latest camera state and fans it out to subscribers. It uses latest-value channels: slow subscribers never build a backlog.

**The page** is not a server (browsers can't be). It dials out to the server:
`ViewerLink.Attach` is a server stream of commands, and `ViewerLink.ReportCamera` is called whenever the camera changes.

---

## Repository layout

```
GrpcViewport/
├─ proto/grpcviewport/v1/
│  ├─ scene.proto            public API: SceneService (Add/Remove/SubscribeCamera) + geometry messages
│  └─ viewer_link.proto      internal: page <-> server (Attach, ReportCamera)
├─ web/                      Babylon.js + TypeScript + Vite page
│  ├─ src/main.ts            scene, camera, connection, keyboard
│  ├─ src/viewer/            bridge-link, geometry-builder, scene-controller, camera-reporter,
│  │                         loading-overlay, frame-watcher, scene-helpers, nav-gizmo, view-toolbar
│  ├─ src/gen/               generated from proto (NOT in git; created by npm install/build)
│  └─ buf.gen.yaml           TypeScript code generation (protoc-gen-es)
├─ dotnet/
│  ├─ GrpcViewport.Server/   .NET 8 class library: Kestrel, SceneHub, gRPC services, validation
│  ├─ GrpcViewport.Host/     .NET 8 exe: hosts the Server for .NET Framework apps (sidecar)
│  ├─ GrpcViewport.WinForms/ .NET Framework 4.8 test client (gRPC-Web, starts the Host)
│  └─ GrpcViewport.Wpf/      .NET 8 test client (Server in-process)
├─ build.ps1                 builds both standalone packages into artifacts/
└─ .gitignore                build output, node_modules, generated code are never committed
```

Generated code and build output are **not** in git. Everyone builds them from source.

---

## Download and run (no build)

1. Open **Releases** on GitHub and download
   - `GrpcViewport-Wpf.zip`: WPF test client, or
   - `GrpcViewport-WinForms.zip`: WinForms test client plus the `host\` sidecar.
2. Extract to any folder and start `GrpcViewport.Wpf.exe` or `GrpcViewport.WinForms.exe`.

Requirements: Windows 10/11 x64 with the **WebView2 Runtime** (preinstalled on Windows 11).
Both packages are **self-contained**: no .NET installation is needed. .NET Framework 4.8 is part of Windows.

Run only **one** of the two apps at a time: both use ports 8080 and 50051.

> The builds are not code-signed. On Windows 11 with **Smart App Control** enabled they are blocked.
> See [Troubleshooting](#troubleshooting).

---

## Build from source

### Prerequisites

| Tool | Version | Used for |
|---|---|---|
| Git | any | clone |
| Node.js | 20+ (22 recommended) | the web page, code generation |
| .NET SDK | 8.0 | Server, Host, WPF |
| Visual Studio 2022 | with **.NET desktop development** workload and **.NET Framework 4.8 targeting pack** | WinForms (classic project, `packages.config`) |
| WebView2 Runtime | Evergreen | running the apps |

`buf` and `protoc-gen-es` come with `npm install`; nothing else to install.

### Build order: the web page first, then .NET

```powershell
git clone https://github.com/<user>/GrpcViewport.git
cd GrpcViewport

# 1. Web page (also generates web/src/gen from proto)
cd web
npm install
npm run build          # -> web/dist
cd ..

# 2. .NET
cd dotnet
dotnet build GrpcViewport.Host
dotnet build GrpcViewport.Wpf
```

Then open the solution in Visual Studio and build **GrpcViewport.WinForms** there.
`dotnet build` cannot restore `packages.config` projects; Visual Studio restores them automatically.

**Why the web page first?** The Host and WPF projects copy `web/dist/**` into their output folder (`viewer\`) at build time.
If `dist` doesn't exist yet, the apps start with an empty page (HTTP 404).

### Code generation

| Language | Tool | Output | When |
|---|---|---|---|
| C# | `Grpc.Tools` (`<Protobuf>` items in the csproj) | `obj/` | every build |
| TypeScript | `buf` + `protoc-gen-es` (`web/buf.gen.yaml`) | `web/src/gen/` | `npm install`, `npm run dev`, `npm run build` (or `npm run gen`) |

After changing a `.proto` file: run `npm run build` (or `npm run gen`), then **Rebuild** the .NET solution.

---

## Development workflow

### Run the WPF client (simplest)

Set **GrpcViewport.Wpf** as the startup project and press F5.
It starts the server in-process, opens the page from `http://127.0.0.1:8080/` and enables the toolbar.

### Run the WinForms client

Set **GrpcViewport.WinForms** as the startup project and press F5.
In development it finds the host in `GrpcViewport.Host\bin\Debug\net8.0\`, so build the Host first.
The solution's **Project Dependencies** make WinForms depend on the Host.

### Live-editing the page with Vite

```powershell
cd web
npm run dev            # http://localhost:5173
```

Point the app's WebView at Vite with the environment variable `VIEWER_DEV_URL=http://localhost:5173`.
For WPF, use `Properties/launchSettings.json`:

```json
{
  "profiles": {
    "GrpcViewport.Wpf": {
      "commandName": "Project",
      "environmentVariables": { "VIEWER_DEV_URL": "http://localhost:5173" }
    }
  }
}
```

A page served by Vite (port 5173) automatically talks to the server on `http://127.0.0.1:8080`.
CORS for the Vite origin is enabled in `ViewerServerOptions.CorsOrigins`.

### Debugging the page

Right-click inside the viewport, choose **Inspect**. You get the Chromium DevTools for the WebView.
In the Console, `scene`, `camera`, `controller`, `helpers`, `gizmo` and `toolbar` are available as globals.

---

## Using the viewer

### Mouse

| Action | Mouse |
|---|---|
| Orbit | left drag |
| Pan | right drag, or Ctrl + left drag |
| Zoom | wheel |

### Keyboard (Blender-style)

| Key | View |
|---|---|
| `1` / `Ctrl+1` | front / back |
| `3` / `Ctrl+3` | right / left |
| `7` / `Ctrl+7` | top / bottom |
| `9` | opposite side of the current view |
| `F` or `Home` | fit all geometry |
| `G` | toggle grid |
| `A` | toggle world axes |

Numpad and the top number row both work. The scene is **Y-up**, right-handed.

### On-screen controls

- **Navigation gizmo** (top right): click an axis ball to look along that axis; click it again to look from the opposite side; drag it to orbit.
- **Toggle buttons** (bottom right): grid and world axes.
- **Status pill**: connection state (`Connected · …`, `Connecting…`, `Offline · <reason>`).
- **Loading overlay**: shown while large messages arrive (> 1 MB, with %) and while large batches (≥ 50,000 vertices) are built.

The grid rescales to the scene size (cell size = power of ten) every time the view is fitted.

---

## The API (proto contract)

Package `grpcviewport.v1`, C# namespace `GrpcViewport.V1`.
`v1` only receives **backwards-compatible** changes: new fields, messages, RPCs or oneof cases.
Breaking changes would go into a new `v2` package.

### `SceneService` (public)

| RPC | Description |
|---|---|
| `AddGeometry(AddGeometryRequest) → GeometryHandle` | Adds one batch; returns its handle. `INVALID_ARGUMENT` with the exact field path if malformed. |
| `RemoveGeometry(RemoveGeometryRequest) → RemoveGeometryResponse` | Removes a batch. `NOT_FOUND` for unknown handles. |
| `SubscribeCamera(SubscribeCameraRequest) → stream CameraState` | Current camera immediately, then every change, at most `max_rate_hz` per second (latest value wins). |

### `AddGeometryRequest`

| Field | Meaning |
|---|---|
| `name` | Optional debug name (visible in the Babylon inspector). |
| `transform` | Optional `Matrix4` applied to the whole batch. |
| `geometry` (oneof) | exactly one of: `polylines`, `point_clouds`, `meshes`, `group` |

| Geometry | Rendering |
|---|---|
| `PolylineBatch` | all polylines → **one** line mesh (one draw call) |
| `PointCloudBatch` | all clouds → **one** points mesh; `point_size` in pixels (default 2) |
| `MeshBatch` | meshes without instances → merged into **one** mesh; each mesh with `instances` → one GPU-instanced draw call |
| `GeometryGroup` | any combination of the three above under **one handle** and one transform (e.g. a box with edges, faces and axes) |

### Conventions

- **Positions**: flat `x, y, z` triplets (`length % 3 == 0`).
- **Colors**:
  - per item: `Color { r, g, b, a }`, channels 0..1. **`a == 0` means opaque**, so `new Color { R = 1 }` is red. Use e.g. `a = 0.07` for very transparent.
  - per vertex (optional): `colors` packed as `0xRRGGBBAA`, exactly one per vertex.
- **Matrix4**: 16 floats, column-major (OpenGL order), translation in `m[12]`, `m[13]`, `m[14]`.
- **Mesh triangles**: counter-clockwise = front face. Normals are computed when omitted. Faces are drawn double-sided.
- **Units** are whatever you use; the camera and grid adapt to the scene size.

### `CameraState`

`position`, `target`, `up`, `fov_y` (radians), `near`, `far`, `view` and `projection` (`Matrix4`, same layout as above), `viewport_width`/`height` (pixels), `sequence` (monotonic, for dropping stale updates), `timestamp_ms` (UTC epoch milliseconds).

### `ViewerLink` (internal, page ↔ server)

`Attach` (stream of `Clear` + full replay + live `Add`/`Remove` commands) and `ReportCamera`. Clients never call these.

---

## Client examples

### .NET 8 in-process (like the WPF app)

Reference `GrpcViewport.Server`. No network hop for your calls; the page still connects over 8080.

```csharp
using GrpcViewport.Server;
using GrpcViewport.V1;

await using var server = await ViewerServer.StartAsync(new ViewerServerOptions
{
    StaticFilesRoot = Path.Combine(AppContext.BaseDirectory, "viewer"),
});

// Show the page: webView.Source = new Uri("http://127.0.0.1:8080/");

GeometryHandle handle = server.Hub.Add(request);   // throws GeometryValidationException if invalid
server.Hub.Remove(handle.Id);                       // false if unknown

// Camera: cancel the token to unsubscribe
var cts = new CancellationTokenSource();
await foreach (CameraState cam in server.Hub.SubscribeCamera(30, cts.Token))
{
    Console.WriteLine($"{cam.Position.X} {cam.Position.Y} {cam.Position.Z}");
}
```

`ViewerServer.StartAsync` throws `IOException` if port 8080 or 50051 is already in use.

### .NET Framework 4.8 via gRPC-Web (like the WinForms app)

Packages: `Grpc.Net.Client`, `Grpc.Net.Client.Web`, `Google.Protobuf`, `Grpc.Tools`;
plus `<Protobuf Include="..\..\proto\grpcviewport\v1\scene.proto" ProtoRoot="..\..\proto" GrpcServices="Client" />`.

```csharp
ServicePointManager.DefaultConnectionLimit = 16;   // the camera stream holds one HTTP/1.1 connection

var handler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler())
{
    HttpVersion = new Version(1, 1),                // .NET Framework only speaks HTTP/1.x
};
var channel = GrpcChannel.ForAddress("http://127.0.0.1:8080", new GrpcChannelOptions
{
    HttpHandler = handler,
    MaxReceiveMessageSize = null,
    MaxSendMessageSize = null,
});
var scene = new SceneService.SceneServiceClient(channel);

GeometryHandle handle = await scene.AddGeometryAsync(request);
await scene.RemoveGeometryAsync(new RemoveGeometryRequest { Handle = handle });

// Camera: cancel the token to unsubscribe
using (var call = scene.SubscribeCamera(new SubscribeCameraRequest { MaxRateHz = 30 }, cancellationToken: ct))
{
    while (await call.ResponseStream.MoveNext(ct))
    {
        CameraState cam = call.ResponseStream.Current;
    }
}
```

Use **one** `GrpcChannel` for the app's lifetime. Start the host with `HostLauncher.Start(...)` before creating the form.

### .NET 8+ via plain gRPC (HTTP/2)

```csharp
var channel = GrpcChannel.ForAddress("http://127.0.0.1:50051");
var scene = new SceneService.SceneServiceClient(channel);
```

The client class comes from your own `<Protobuf … GrpcServices="Client" />` item, or set `GrpcServices="Both"` for `scene.proto` in `GrpcViewport.Server.csproj`.

### Example geometry

```csharp
// 20 rings in ONE request = one draw call
var batch = new PolylineBatch();
for (int k = 0; k < 20; k++)
{
    var ring = new Polyline { Closed = true, Color = new Color { R = 0.3f, G = 0.8f, B = 1f } };
    for (int i = 0; i < 64; i++)
    {
        double a = i / 64.0 * 2 * Math.PI;
        ring.Positions.Add((float)Math.Cos(a) * (1 + k * 0.1f));
        ring.Positions.Add(k * 0.05f);
        ring.Positions.Add((float)Math.Sin(a) * (1 + k * 0.1f));
    }
    batch.Polylines.Add(ring);
}
var request = new AddGeometryRequest { Name = "rings", Polylines = batch };
```

More examples (point clouds, instanced boxes, a tilted "scene segment" group with transparent faces and local axes) are in `dotnet/GrpcViewport.WinForms/DemoGeometry.cs`.

---

## Ports and endpoints

All endpoints bind to **127.0.0.1 only**, not reachable from other machines.

| Port | Protocol | Used by |
|---|---|---|
| 50051 | HTTP/2, plain gRPC | .NET 8+ and other gRPC clients |
| 8080 | HTTP/1.1, gRPC-Web + static files | the page (`/`), .NET Framework clients |

Change them with `ViewerServerOptions.GrpcPort` / `WebPort`. The page always uses the origin it was loaded from.

---

## Releasing

Releases are zips attached to a **GitHub Release**. Binaries never go into the git history.

### 1. Build the packages

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

This runs `npm ci` and `npm run build`, publishes WPF (self-contained, win-x64), builds WinForms (Release, via Visual Studio's MSBuild), publishes the Host into `host\` next to it, and zips both:

```
artifacts/
├─ GrpcViewport-Wpf.zip
└─ GrpcViewport-WinForms.zip
```

### 2. Publish

With the GitHub CLI (one-time: `winget install --id GitHub.cli`, reopen PowerShell, `gh auth login`):

```powershell
git push
gh release create v0.2.0 artifacts\GrpcViewport-Wpf.zip artifacts\GrpcViewport-WinForms.zip --title "v0.2.0" --generate-notes
```

Use a new version number each time. To replace the zips of an existing release:

```powershell
gh release upload v0.2.0 artifacts\GrpcViewport-Wpf.zip artifacts\GrpcViewport-WinForms.zip --clobber
```

Alternatively, publish by hand: **Releases → Draft a new release**, create the tag, drag both zips into "Attach binaries", then **Publish release**.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| "Port 50051 or 8080 on 127.0.0.1 is already in use" | Another instance, or a leftover `GrpcViewport.Host.exe` | Close the other app; end `GrpcViewport.Host.exe` in Task Manager. |
| Page is empty / HTTP 404 | `web/dist` wasn't built before the .NET build | `npm run build` in `web/`, then **Rebuild** Host / WPF. |
| New page features don't appear | Output folder still has the old `dist`, or WebView cache | Rebuild; if VS didn't pick up new file names, close and reopen the solution. Clear the cache: delete `%LOCALAPPDATA%\GrpcViewport.Wpf\WebView2` (or `…\GrpcViewport\WebView2` for WinForms). |
| Status pill: `Offline · [unknown] HTTP 415` | Endpoint not gRPC-Web enabled, or plain gRPC transport used | Server must call `UseGrpcWeb()` + `.EnableGrpcWeb()`; the page must use `createGrpcWebTransport`. |
| Status pill: `Offline · …` and reconnecting | Server not running, or an exception while applying a command | Check the app is running; open Inspect → Console for errors. |
| Camera subscription never receives anything | The page doesn't call `ReportCamera` (old build or missing `camera-reporter.ts`) | Check `web/src/viewer/camera-reporter.ts` exists and `npm run build` has no errors; rebuild; clear the WebView cache. |
| `Exception thrown: 'System.OperationCanceledException'` in VS Output | First-chance notice when a stream is cancelled (e.g. unsubscribing on close) | Harmless. Hide via Output window → untick **Exception Messages**. |
| `Cannot find module './viewer/…'` in `npm run build` | File in the wrong folder or with the wrong name (`.ts.txt`) | Put it in `web/src/viewer/` with the exact name. |
| `'Color' is an ambiguous reference` | `using System.Drawing;` next to `using GrpcViewport.V1;` | Remove `using System.Drawing;` from files that build proto messages. |
| Windows blocks the app ("Smart App Control") | Unsigned executables | Unblock the zip before extracting (Properties → Unblock). Real fix: sign the `GrpcViewport*.exe/.dll` files with a trusted code-signing certificate. |
| `.\build.ps1` opens Notepad | Typed into Command Prompt | Use `powershell -ExecutionPolicy Bypass -File .\build.ps1`. |
| "running scripts is disabled on this system" | PowerShell execution policy | Same command as above, or once: `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`. |
| `gh` is not recognized | GitHub CLI missing, or PATH not refreshed | `winget install --id GitHub.cli`, then open a **new** terminal (restart VS/VS Code if using their terminal). |
| Git tries to add `.vs`, `bin`, `obj`, … | `.gitignore` saved as UTF-16 or named `.gitignore.txt` | Save it as UTF-8 named exactly `.gitignore` in the repo root; check with `git check-ignore -v dotnet/.vs`. |
