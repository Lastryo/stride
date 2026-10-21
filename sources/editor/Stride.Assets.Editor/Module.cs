// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Assets.Presentation;
using Stride.Core.Reflection;

namespace Stride.Assets.Editor;

internal class Module
{
    [Core.ModuleInitializer]
    public static void Initialize()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(Stride.Assets.Models.ModelAsset).Module.ModuleHandle);
        AssemblyRegistry.Register(typeof(Module).Assembly, AssemblyCommonCategories.Assets);
        AssetsPlugin.RegisterPlugin(typeof(StrideEditorPlugin));
    }
}
