# Avalonia Game Studio appearance

The editor shell follows the default Game Studio layout shown in the
[official interface guide](https://doc.stride3d.net/4.3/en/manual/game-studio/index.html).
The red outlines in that guide's numbered screenshot are annotations, not
editor chrome.

The dark palette uses `ExpressionDarkTheme.xaml` from
`sources/presentation/Stride.Core.Presentation.Wpf/Themes/Overrides/` as its
source: background `#1A1A1D`, panel `#101011`, border `#333333`, text `#E6E6E6`,
and blue selection/status `#007ACC`. A corresponding light palette remains
available through the existing theme setting.

`sources/editor/Stride.GameStudio.Avalonia/Themes/GameStudioTheme.axaml` contains
the shell brushes, compact spacing, and Fluent resource overrides. The theme
is loaded after the shared control styles and is confined to Game Studio.

The main layout keeps the editor and Property Grid above Solution Explorer,
Asset View, and Asset Preview. The property and preview panels align to the
right; asset and preview tabs sit at the bottom. Splitters remain resizable.
There are no simulated build/play controls: the command strip binds only to
existing Open, Undo, and Redo commands. Asset thumbnails retain their real
binding and use a document silhouette when a thumbnail is unavailable.

Validation: build the Avalonia desktop project, then open AnimatedModel as
described in `linux-first-steps.md`. Check headers, tabs, resize behavior,
tree selection, and asset cards. This change styles existing views; it does
not implement docking, missing menus, asset creation, or a 3D viewport.
