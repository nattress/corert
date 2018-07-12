
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

        private readonly mdToken _token;

        public MethodImport(ImportSectionNode table, MethodDesc methodDesc, mdToken token)
            : base(table, new MethodFixupSignature(ReadyToRunFixupKind.READYTORUN_FIXUP_MethodEntry_RefToken, methodDesc, token))
        {
            _methodDesc = methodDesc;
            _token = token;
        }

        public MethodDesc Method => _methodDesc;

        int ISortableSymbolNode.ClassCode => throw new NotImplementedException();

        int ISortableSymbolNode.CompareToImpl(ISortableSymbolNode other, CompilerComparer comparer)
        {
            return _token.CompareTo(((MethodImport)other)._token);
        }

        protected override string GetName(NodeFactory context)
        {
            return "MethodImport: " + Method.ToString();
        }
    }
}
