// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Reflection.PortableExecutable;

using Internal.Text;
using Internal.TypeSystem.Ecma;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    /// <summary>
    /// Copies the native PE resources into the output image
    /// </summary>
    public class CopiedNativeResourcesNode : ObjectNode, ISymbolDefinitionNode
    {
        private EcmaModule _module;

        public CopiedNativeResourcesNode(EcmaModule module)
        {
            _module = module;

            Debug.Assert(Size > 0);
        }

        public override ObjectNodeSection Section => ObjectNodeSection.TextSection;

        public override bool IsShareable => false;

        public override int ClassCode => 345345;

        public override bool StaticDependenciesAreComputed => true;

        public int Offset => 0;

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix);
            sb.Append("__NativeResources");
        }

        protected override string GetName(NodeFactory factory) => this.GetMangledName(factory.NameMangler);

        public int Size => _module.PEReader.PEHeaders.PEHeader.ResourceTableDirectory.Size;

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            if (relocsOnly)
            {
                return new ObjectData(
                    data: Array.Empty<byte>(),
                    relocs: Array.Empty<Relocation>(),
                    alignment: 1,
                    definedSymbols: new ISymbolDefinitionNode[] { this });
            }

            ObjectDataBuilder builder = new ObjectDataBuilder(factory, relocsOnly);
            builder.RequireInitialAlignment(4);
            builder.AddSymbol(this);

            DirectoryEntry resourcesDirectory = _module.PEReader.PEHeaders.CorHeader.ResourcesDirectory;
            PEMemoryBlock block = _module.PEReader.GetSectionData(resourcesDirectory.RelativeVirtualAddress);
            builder.EmitBytes(block.GetReader().ReadBytes(resourcesDirectory.Size));

            return builder.ToObjectData();
        }
    }
}
