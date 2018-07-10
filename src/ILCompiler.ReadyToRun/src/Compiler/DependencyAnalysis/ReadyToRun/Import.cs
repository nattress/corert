// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Internal.Text;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    /// <summary>
    /// This class represents a single indirection cell in one of the import tables.
    /// </summary>
    public class Import : EmbeddedObjectNode, ISymbolDefinitionNode
    {
        public readonly ImportSectionNode Table;

        internal readonly Signature ImportSignature;

        public Import(ImportSectionNode tableNode, Signature importSignature)
        {
            Table = tableNode;
            ImportSignature = importSignature;
        }

        protected override string GetName(NodeFactory factory)
        {
            return "Import->" + ImportSignature.GetMangledName(factory.NameMangler);
        }

        protected override int ClassCode => 667823013;

        public virtual bool IsDelayed => Table.IsDelayed;

        public override void EncodeData(ref ObjectDataBuilder dataBuilder, NodeFactory factory, bool relocsOnly)
        {
            // This needs to be an empty target pointer since it will be filled in with Module*
            // when loaded by CoreCLR
            dataBuilder.EmitZeroPointer();
        }

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append($@"Fixup: table = {Table.Index}; signature = ");
            ImportSignature.AppendMangledName(nameMangler, sb);
        }

        public override bool StaticDependenciesAreComputed => true;

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            yield return new DependencyListEntry(ImportSignature, "Signature for ready-to-run fixup import");
        }

        int ISymbolDefinitionNode.Offset => OffsetFromBeginningOfArray;
        int ISymbolNode.Offset => 0;
    }
}
