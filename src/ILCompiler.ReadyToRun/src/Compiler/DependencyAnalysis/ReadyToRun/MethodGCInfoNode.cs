// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Internal.Text;

namespace ILCompiler.DependencyAnalysis
{
    class MethodGCInfoNode : EmbeddedObjectNode, ISymbolDefinitionNode
    {
        private readonly MethodCodeNode _methodNode;

        public MethodGCInfoNode(MethodCodeNode methodNode)
        {
            _methodNode = methodNode;
        }

        public override bool StaticDependenciesAreComputed => true;

        public int Offset => 0;

        protected override int ClassCode => 892356612;

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append("MethodGCInfoNode->");
            _methodNode.AppendMangledName(nameMangler, sb);
        }

        public override void EncodeData(ref ObjectDataBuilder dataBuilder, NodeFactory factory, bool relocsOnly)
        {
            byte[] gcInfo = _methodNode.GCInfo;
            if (gcInfo != null && gcInfo.Length != 0)
            {
                dataBuilder.EmitBytes(gcInfo);
            }
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory context)
        {
            return Array.Empty<DependencyListEntry>();
        }

        protected override string GetName(NodeFactory context)
        {
            Utf8StringBuilder sb = new Utf8StringBuilder();
            sb.Append("MethodGCInfo->");
            _methodNode.AppendMangledName(context.NameMangler, sb);
            return sb.ToString();
        }
    }
}
