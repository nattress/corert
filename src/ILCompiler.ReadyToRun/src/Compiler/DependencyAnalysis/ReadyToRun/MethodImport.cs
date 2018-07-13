
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

using Internal.JitInterface;
using Internal.TypeSystem;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    public class MethodImport : Import, ISymbolDefinitionNode, IMethodNode
    {
        private readonly MethodDesc _methodDesc;

        private readonly mdToken _token;

        private readonly MethodWithGCInfo _localMethod;

        public MethodImport(ImportSectionNode table, ReadyToRunFixupKind fixupKind, MethodDesc methodDesc, mdToken token, MethodWithGCInfo localMethod = null)
            : base(table, new MethodFixupSignature(fixupKind, methodDesc, token))
        {
            _methodDesc = methodDesc;
            _token = token;
            _localMethod = localMethod;
        }

        public MethodDesc Method => _methodDesc;

        int ISortableSymbolNode.ClassCode => throw new NotImplementedException();

        int ISortableSymbolNode.CompareToImpl(ISortableSymbolNode other, CompilerComparer comparer)
        {
            return _token.CompareTo(((MethodImport)other)._token);
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            if (_localMethod == null)
            {
                return base.GetStaticDependencies(factory);
            }
            return new DependencyListEntry[]
            {
                new DependencyListEntry(_localMethod, "Local method called through R2R helper"),
                new DependencyListEntry(ImportSignature, "Signature for ready-to-run fixup import"),
            };
        }

        protected override string GetName(NodeFactory context)
        {
            return "MethodImport: " + Method.ToString();
        }
    }
}
