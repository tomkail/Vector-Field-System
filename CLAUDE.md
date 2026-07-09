# CLAUDE.md

Guidance for working in the **Vector Field System** Unity project.

## Unity MCP server — which one to use

Two Unity MCP servers are connected in this environment. **When I say "the Unity MCP server", I always mean the community "MCP for Unity" server, NOT the official Unity one.**

- ✅ **Use this one** — tools prefixed `mcp__UnityMCP__*` (e.g. `mcp__UnityMCP__read_console`, `manage_editor`, `manage_gameobject`, `manage_scene`, `execute_code`). This is the CoplayDev "MCP for Unity" server.
- ❌ **Not this one** — tools prefixed `mcp__unity-mcp__*` (e.g. `Unity_RunCommand`, `Unity_Camera_Capture`). This is the official Unity server; don't use it unless I explicitly ask for "the official one".

If the Unity Editor isn't attached, `mcp__UnityMCP__*` tools return `no_unity_session` even though the server itself is up — ask me to open the Editor rather than assuming the server is broken.

## Project overview

A Unity plugin for authoring and rendering 2D vector fields, with a brush/painting system, debug visualization, and demos. Everything lives under `Assets/Vector Fields/`:

- `Vector Field/` — core field types and the `VectorFieldComponent` (scene components; the goal is a code-usable core with components as thin editor wrappers).
- `Brush/` — painting system: `PaintBrush`, `VectorFieldStroke`, brush ops (`Ops/`), `GpuRegionUploader` (uploads only the dirty brush region to the GPU — never a full per-frame `Texture2D.Apply`). See `Brush/RUNTIME_PAINTING_SPEC.md`.
- `Debug Renderer/` — scene-view arrow visualization + per-user settings.
- `Shaders/` — flow visualization etc.
- `Examples/` — demo scenes.
- `Tests/` — edit-mode tests (`Tests/Editor/`).

The plugin has assembly definitions: `VectorFields` (runtime, asmdef at the folder root), `VectorFields.Editor` (all editor code, consolidated under `Editor/` mirroring the runtime layout), and `VectorFields.Tests.Editor` (`Tests/Editor/`). The optional `com.unity.splines` dependency is wired through the runtime asmdef: a GUID reference to `Unity.Splines` plus a `versionDefines` entry that sets `VECTOR_FIELDS_SPLINES` only while the package is installed (guards `SplineVectorFieldComponent`).

## Key docs (keep in sync)

- `TODO.md` — living checklist / current work.
- `HANDOVER.md` — full context.
- `VECTOR_FIELDS.md` — public API reference. **Update this when the public API changes** (per `DOCS_GUIDE.md`).
- `DOCS_GUIDE.md` — documentation conventions.
- `Assets/Vector Fields/Brush/RUNTIME_PAINTING_SPEC.md` — intended runtime painting design.

## Conventions

- Fields are stored on the component (scene objects, not assets); the scene serialization format is a project setting.
- After creating/editing scripts, check `mcp__UnityMCP__read_console` for compilation errors before using new types, and poll the editor state's `isCompiling` flag to know when domain reload finishes.
- Heads-up: other agents sometimes run broad `git add -A` / commits on shared branches — be careful staging changes.
