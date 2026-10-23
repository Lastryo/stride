// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;

namespace Stride.Debugger.Target
{
    /// <summary>
    /// Represents a loaded assembly in the debug process.
    /// </summary>
    [Serializable]
    public struct DebugAssembly : IEquatable<DebugAssembly>
    {
        public int Id { get; set; }

        public static readonly DebugAssembly Empty = new DebugAssembly(0);

        internal DebugAssembly(int id)
        {
            Id = id;
        }

        public bool Equals(DebugAssembly other)
        {
            return Id == other.Id;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is DebugAssembly && Equals((DebugAssembly)obj);
        }

        public override int GetHashCode()
        {
            return Id;
        }
    }
}
