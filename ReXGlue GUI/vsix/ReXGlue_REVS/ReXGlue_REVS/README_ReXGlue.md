# ReXGlue Visual Studio Extension

Automate ReXGlue codegen from Visual Studio: on breakpoint, read `ctx.ctr.u32`, inject addresses into your TOML `[functions]`, run `rexglue codegen`, then build and continue debugging.

---

## Build

Build in **Visual Studio** (the VSIX needs the VS SDK and EnvDTE from your installation):

1. Open the solution (e.g. `ReXGlue_REVS.slnx`) in Visual Studio.
2. **Build → Build Solution**.
3. Run or deploy the VSIX (F5 to start Experimental Instance, or install the built `.vsix`).

---

## Quick start (in Visual Studio)

1. **Tools → ReXGlue** or **View → Other Windows → ReXGlue** — open the ReXGlue tool window.
2. **Tools → SDK Documentation (Wiki)** — links to the [rexglue-sdk wiki](https://github.com/rexglue/rexglue-sdk/wiki) (Getting Started, TOML reference, CLI, codegen pipeline). **Tools → Migrate Project** runs `rexglue migrate`.
3. In the panel: **Set TOML Path (Solution)** — use your `*_config.toml` from `rexglue init` (e.g. `mygame_config.toml`). After **Initialize New Project**, the extension picks that file automatically when possible.
4. **Code generation** section: optional **`--force`**, **`--enable_exception_handlers`**, and **log level** apply to **Run Code Generation**, **Fetch Once**, and **Auto Cycle**.
5. Use **Fetch Once (codegen)** or **Auto Cycle** (see workflow below). Output appears in the tool window **Output** area.

---

## Full workflow

### 1. Select the SDK

Choose the ReXGlue SDK you will use for the game.

### 2. Create a new project (the game)

Using the **ReXGlue GUI** (or the SDK), create a new project — the game project. The GUI’s **Initialize New Project** creates the folder and runs `rexglue init`; it also creates **`.vs/launch.vs.json`** for that project so you can start debugging with the correct target and args (e.g. `assets` path, `--enable_console=true`). This is the solution you will open in Visual Studio and debug.

### 3. Optional: set jump addresses in TOML

In your ReXGlue TOML config you can optionally add:

```toml
setjmp_address  = 0x80000000
longjmp_address = 0x8xxxxxxxxx
```

(Replace with the actual addresses for your target.)

### 4. First gen and capture addresses

- Run **gen** (e.g. `rexglue codegen` or **ReXGlue: Fetch Once** in VS).
- From the **output** of that run, capture some addresses and add them to the TOML `[functions]` section (or leave `[functions]` empty and let the debug loop fill it).

### 5. Generated files

After codegen, ReXGlue produces a **generated** folder for the game, for example:

- `generated/sources.cmake` — list of generated sources (include in your CMake build).
- `generated/<game>_config.cpp`, `generated/<game>_init.cpp`.
- `generated/<game>_recomp.0.cpp`, `<game>_recomp.1.cpp`, … — recompiled function stubs.

Example layout (e.g. Battlefield 1943):

```
Battlefield_1943/
  generated/
    sources.cmake
    battlefield_1943_config.cpp
    battlefield_1943_init.cpp
    battlefield_1943_recomp.0.cpp
    battlefield_1943_recomp.1.cpp
    ...
```

Wire `sources.cmake` (or the listed `.cpp` files) into your project so the game build uses the generated code.

### 6. Open the project and prepare for debugging

- Open the **game project** in Visual Studio.
- Open the **ReXGlue** tool window (**Tools → ReXGlue** or **View → Other Windows → ReXGlue**) and click **Set TOML Path (Solution)**. The path is stored per solution under `.vs/ReXGlue/toml_path.txt`.
- Start debugging. The extension uses **`ctx.ctr.u32`** when you hit a breakpoint.  
  **Most games won’t have `ctx` at first** — add it (e.g. expose a `ctx` in your runtime so the debugger can evaluate `ctx.ctr.u32` at the breakpoint).

### 7. Auto cycle: break → update TOML → codegen → repeat

Once `ctx` is available and you’re breaking where `ctx.ctr.u32` is valid:

1. In the **ReXGlue** tool window, turn **Auto Cycle** **on**.
2. Start debugging and hit a breakpoint where `ctx` is in scope.
3. On break, the extension:
   - Grabs the address from **`ctx.ctr.u32`**.
   - Updates the TOML **`[functions]`** with that address (no duplicates).
   - **Stops debugging**.
   - Runs **`rexglue codegen "<toml_path>"`**.
   - When codegen **finishes**, it builds the solution and **starts debugging again** so you can hit the next break and capture more addresses.

So the loop is: **Debug → Break → Grab address → Update TOML → Stop debug → Codegen → (when done) build & start debug again → repeat.**

Turn **off** Auto Cycle when you’re done (same command or tool window button).

---

## Opening ReXGlue

- **Tools → ReXGlue** or **View → Other Windows → ReXGlue** — opens the ReXGlue tool window. All features live there (no separate menu commands).

**ReXGlue tool window** (same feature set as the desktop ReXGlue GUI):
- **TOML**: path display, **[functions]** count, **Set TOML Path**, **Open TOML in Editor**
- **TOML editor**: **Reload**, **Save**; toolbar: **+ setjmp/longjmp**, **Remove Dupes**, **Clear Values**, **Write [functions]**, **Add addr = {}**, **Write [rexcrt]**; **Starter** templates (= {}, = { name, size }, = { parent, size }); **Save Backup**, **Load Backup**
- **Code generation**: **Fetch Once (codegen)**, **Auto Cycle** (on break → update TOML → stop debug → codegen → build & start again)
- **Address Parser**: paste text, **Parse** (" from 0x..." → ADDR = {}), **Copy output**, **Add to [functions]**
- **Output**: **Open ReXGlue output pane** (View → Output → ReXGlue)

All codegen and build output appears in the **ReXGlue** pane (**View → Output**, “Show output from” → ReXGlue).

---

## If commands don’t appear

- **Rebuild** the ReXGlue_REVS project, then **uninstall** the extension (Extensions → Manage Extensions → ReXGlue → Uninstall), then install again from the new `.vsix` (or run with F5 to deploy to Experimental Instance).
- Check **View → Other Windows → ReXGlue** as well as **Tools** (the extension adds the tool window to both).
- Reset the Experimental Instance: run **Reset the Visual Studio 2022 Experimental Instance** from the Start menu, then start VS and open a solution.
- From a **Developer Command Prompt**, with VS closed, run: `devenv /updateConfiguration`.

---

## Requirements

- **Visual Studio 2022** (or version matching the VSIX).
- **rexglue** on `PATH` so the extension can run `rexglue codegen "<toml path>"`.
- ReXGlue TOML with a **`[functions]`** section for address injection.
- Your game/runtime must expose **`ctx`** (and `ctx.ctr.u32`) at the breakpoint so the debugger can evaluate it; add it if the game doesn’t have it by default.
