// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Internal.TypeSystem;

namespace ILCompiler.DependencyAnalysis
{
    public class MethodWithGCInfo : MethodCodeNode
    {
        public readonly MethodGCInfoNode GCInfoNode;

        public MethodWithGCInfo(MethodDesc methodDesc)
            : base(methodDesc)
        {
            GCInfoNode = new MethodGCInfoNode(this);
        }

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly)
        {
            ObjectData methodCode = base.GetData(factory, relocsOnly);
            if (relocsOnly)
            {
                Relocation[] relocs = new Relocation[methodCode.Relocs.Length + 1];
                Array.Copy(methodCode.Relocs, relocs, methodCode.Relocs.Length);
                relocs[methodCode.Relocs.Length] = new Relocation(RelocType.IMAGE_REL_BASED_ADDR32NB, 0, GCInfoNode);
                methodCode = new ObjectData(methodCode.Data, relocs, methodCode.Alignment, methodCode.DefinedSymbols); 
            }
            return methodCode;
        }
    }
}
