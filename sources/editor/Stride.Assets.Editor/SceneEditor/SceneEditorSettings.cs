// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Settings;
using CoreEditorSettings = Stride.Core.Assets.Editor.Settings.EditorSettings;

namespace Stride.Assets.Editor.SceneEditor;

public static class SceneEditorSettings
{
    public static SettingsKey<bool> AskBeforeDeletingEntities { get; } =
        new SettingsKey<bool>("SceneEditor/AskBeforeDeletingEntities", CoreEditorSettings.SettingsContainer, true);

    public static void Save()
    {
        CoreEditorSettings.Save();
    }
}
