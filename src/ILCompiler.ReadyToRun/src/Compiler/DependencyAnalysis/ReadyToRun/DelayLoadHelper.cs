// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Internal.Text;
using Internal.TypeSystem;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    /// <summary>
    /// This node emits a thunk calling DelayLoad_Helper with a given instance signature
    /// to populate its indirection cell.
    /// </summary>
    public class DelayLoadHelper : ObjectNode, ISymbolDefinitionNode
    {
        private readonly ISymbolNode _helperCell;

        private readonly Signature _instanceSignature;

        private readonly Import _instanceCell;

        private readonly ISymbolNode _moduleImport;

        public DelayLoadHelper(ReadyToRunCodegenNodeFactory factory, Signature instanceSignature)
        {
            _helperCell = factory.GetReadyToRunHelperCell(ReadyToRunHelper.READYTORUN_HELPER_DelayLoad_Helper);
            _instanceSignature = instanceSignature;
            _instanceCell = new Import(factory.HelperImports, instanceSignature);
            factory.HelperImports.AddImport(factory, _instanceCell);
            _moduleImport = factory.ModuleImport;
        }

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append("DelayLoadHelper->");
            _instanceSignature.AppendMangledName(nameMangler, sb);
        }

        protected override string GetName(NodeFactory factory)
        {
            return "DelayLoadHelper";
        }

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly)
        {
            ObjectDataBuilder builder = new ObjectDataBuilder();
            builder.AddSymbol(this);

            switch (factory.Target.Architecture)
            {
                case TargetArchitecture.X64:
                    {
                        builder.RequireInitialAlignment(8);

                        // lea rax, [pCell]
                        builder.EmitByte(0x48);
                        builder.EmitByte(0x8D);
                        builder.EmitByte(0x05);
                        builder.EmitReloc(_instanceCell, RelocType.IMAGE_REL_BASED_REL32);

                        // push table index
                        builder.EmitByte(0x6A);
                        builder.EmitByte((byte)_instanceCell.Table.Index);

                        // push [module]
                        builder.EmitByte(0xFF);
                        builder.EmitByte(0x35);
                        builder.EmitReloc(_moduleImport, RelocType.IMAGE_REL_BASED_REL32);

                        // TODO: additional tricks regarding UNIX AMD64 ABI

                        // jmp [helper]
                        builder.EmitByte(0xFF);
                        builder.EmitByte(0x25);
                        builder.EmitReloc(_helperCell, RelocType.IMAGE_REL_BASED_REL32);

                        break;
                    }

                default:
                    throw new NotImplementedException();
            }

            return builder.ToObjectData();
        }

        public override ObjectNodeSection Section => ObjectNodeSection.TextSection;

        int ISymbolDefinitionNode.Offset => 0;
        int ISymbolNode.Offset => 0;
        public override bool IsShareable => false;
        public override bool StaticDependenciesAreComputed => true;
        protected override int ClassCode => 433266948;
    }
}
