
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

using Internal.JitInterface;
using Internal.TypeSystem;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    public class MethodImport : Import, ISymbolDefinitionNode, IMethodNode
    {
        private readonly MethodDesc _methodDesc;

        public MethodImport(ImportSectionNode table, MethodDesc methodDesc, mdToken token)
            : base(table, new MethodImportSignature(methodDesc, token))
        {
            _methodDesc = methodDesc;
        }

        public override void EncodeData(ref ObjectDataBuilder dataBuilder, NodeFactory factory, bool relocsOnly)
        {
            if (Marked)
            {
                base.EncodeData(ref dataBuilder, factory, relocsOnly);
            }
        }

        public MethodDesc Method => _methodDesc;

        int ISortableSymbolNode.ClassCode => throw new NotImplementedException();

        int ISortableSymbolNode.CompareToImpl(ISortableSymbolNode other, CompilerComparer comparer)
        {
            MethodImport methodFixupCell = (MethodImport)other;
            // TODO: what are we supposed to do here?
            return 0;
        }

        protected override string GetName(NodeFactory context)
        {
            return "MethodImport: " + Method.ToString();
        }
    }
}
