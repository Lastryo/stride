# Editor Framework — Overview

Stride's editor is built on a ViewModel layer that sits above Quantum and the asset system. Every mutation goes through a transaction stack that feeds undo/redo. A separate selection history stack, sharing the same low-level infrastructure, powers back/forward navigation. Both systems use `ITransactionStack` and `Operation` from the `Stride.Core.Transactions` namespace (assembly: `Stride.Core.Design`).

## Shared Infrastructure

```mermaid
flowchart TD
    A["ITransactionStack + Operation<br/>Stride.Core.Design · Stride.Core.Transactions<br/>sources/core/Stride.Core.Design/Transactions/<br/>Shared interface and base type — each consumer creates its own instance"]
    B["IUndoRedoService<br/>Stride.Core.Presentation<br/>sources/presentation/Stride.Core.Presentation/Services/<br/>Wraps stack with naming, dirtiable sync, and save-point tracking"]
    C["SelectionService<br/>Stride.Core.Assets.Editor<br/>sources/editor/Stride.Core.Assets.Editor/Services/<br/>Separate unbounded stack; records selection snapshots"]

    A -. "uses" .-> B
    A -. "uses" .-> C
```

## Projects

The editor codebase spans `sources/presentation/` (MVVM framework, Quantum-to-UI binding, shared controls) and `sources/editor/` (editor infrastructure and concrete asset editors). ViewModels are platform-agnostic; WPF coupling belongs in XAML files, code-behind, and WPF-specific service implementations. See [projects.md](projects.md) for the full project map and assembly reference.

## When You Need These Systems

> **Decision tree:**
>
> - Wrapping a property or collection mutation so it is undoable?
>   → **`IUndoRedoService.CreateTransaction()` + `AnonymousDirtyingOperation`.** See [undo-redo.md](undo-redo.md).
>
> - Writing a reusable operation that merges consecutive edits on the same target?
>   → **`DirtyingOperation` subclass + `IMergeableOperation`.** See [undo-redo.md](undo-redo.md).
>
> - Tracking which objects are "dirty" (unsaved) after changes?
>   → **`IDirtiable` / `DirtiableManager`.** See [undo-redo.md](undo-redo.md).
>
> - Mutating a value through a Quantum node presenter (property grid edit)?
>   → **No `PushOperation` needed** — `ContentValueChangeOperation` is pushed automatically by the Quantum infrastructure. See [undo-redo.md](undo-redo.md#how-quantum-feeds-the-stack-automatically).
>
> - Understanding how back/forward selection history works?
>   → **`SelectionService`.** See [navigation.md](navigation.md).
>
> - Creating a dedicated editing surface for a new asset type?
>   → **Write a custom editor.** See [custom-editor.md](custom-editor.md).
>
> - Understanding or modifying an existing editor?
>   → **Existing editors catalogue.** See [editors.md](editors.md).

## Spoke Files

| File | Covers |
|---|---|
| [undo-redo.md](undo-redo.md) | `ITransactionStack`, `IUndoRedoService`, `DirtyingOperation`, `IMergeableOperation`, `IDirtiable`, dirty-flag synchronisation |
| [navigation.md](navigation.md) | `SelectionService`, selection history snapshots, back/forward navigation |
| [projects.md](projects.md) | Project inventory, WPF boundary rule, assembly map |
| [custom-editor.md](custom-editor.md) | Custom asset editor: base class, registration, lifecycle, services, MVVM patterns |
| [editors.md](editors.md) | Existing editors catalogue: SpriteSheet, Scene, Prefab, UIPage, UILibrary, GraphicsCompositor, Script, VisualScript |
| [avalonia-hot-reload.md](avalonia-hot-reload.md) | Avalonia assembly reload, source watching, state preservation, Undo/Redo and smoke verification |
| [avalonia-live-play.md](avalonia-live-play.md) | LivePlay child game, assembly updates, runtime state preservation and verification |
| [avalonia-script-editor.md](avalonia-script-editor.md) | Avalonia C# editor, Roslyn completion, parameter help, code actions, rename, metadata navigation and source saving |
| [avalonia-curve-editor.md](avalonia-curve-editor.md) | Curve channels, key editing, Quantum Undo/Redo and Avalonia verification |
