# Prompt: Generate the Vector Field System documentation

Use this prompt to (re)generate the user‑facing reference for the Unity **Vector Field System**. Hand it to an agent verbatim; it produces `VECTOR_FIELDS.md`.

---

You are documenting the Unity "Vector Field System" project. Produce a single user‑facing reference markdown file describing every feature.

**Authoritative spec:** Read `DOCS_GUIDE.md` in the repo root and follow it exactly. It defines the house style, the section→code mapping, the extraction procedure, what to exclude, and the verification checklist. The code under `Assets/Vector Fields/` (plus the grid types `Vector2Map`/`TypeMap` under `Assets/UnityX/`) is the source of truth.

**Important:** Generate the documentation fresh from the code and the guide. Do **not** read any existing `VECTOR_FIELDS.md` — the goal is to reproduce it from source, not copy it. Do all the reading and writing **yourself in this one agent** — do not delegate to sub‑agents. The deliverable is the written file; don't finish until it exists.

**Steps**
1. Read `DOCS_GUIDE.md`.
2. Inventory `Assets/Vector Fields/**/*.cs` and map files to sections per the guide; add/drop sections to match what actually exists.
3. Extract the public surface of each subsystem. Efficient approach (per the guide): three read‑only passes — (a) field components, (b) core/utilities/cookie, (c) tools/visualisation. For each item capture its one‑line purpose, the meaningful Inspector fields, the public methods/signatures a user calls, and whether a snippet helps.
4. Write the reference in the house style: one section per feature, **usage‑first**, **no internals**, short code snippets **only** where calling convention or setup order isn't obvious, a table of contents, and tables for enumerations (brush ops, gestures, modes). Use real type/member names.
5. Verify against the guide's checklist (every name/signature exists; the brush‑ops table matches `VectorFieldBrushOpRegistry.Groups`; the painting‑tool gestures/shortcuts match `VectorFieldDrawingTool`). Flag anything you couldn't confirm rather than guessing.

**Output:** write the finished document to `VECTOR_FIELDS.md` in the repo root.
