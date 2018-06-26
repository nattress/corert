// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;
using ILCompiler.DependencyAnalysisFramework;
using Internal.Runtime;
using Internal.Text;
using Internal.TypeSystem;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    class RvaEmbeddedPointerIndirectionNode<TTarget> : EmbeddedPointerIndirectionNode<TTarget>
        where TTarget : ISortableSymbolNode
    {
        public RvaEmbeddedPointerIndirectionNode(TTarget target)
            : base(target) {}

        protected override string GetName(NodeFactory factory) => $"Embedded pointer to {Target.GetMangledName(factory.NameMangler)}";

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            return new[]
            {
                    new DependencyListEntry(Target, "reloc"),
                };
        }

        public override void EncodeData(ref ObjectDataBuilder dataBuilder, NodeFactory factory, bool relocsOnly)
        {
            dataBuilder.RequireInitialPointerAlignment();
            dataBuilder.EmitReloc(Target, RelocType.IMAGE_REL_BASED_ADDR32NB);
        }

        protected override int ClassCode => -66002498;
    }
    public abstract class ReadyToRunSignature : ObjectNode, ISymbolDefinitionNode, ISortableSymbolNode
    {
        public override ObjectNodeSection Section => ObjectNodeSection.DataSection;
        public override bool IsShareable => true;
        public override bool StaticDependenciesAreComputed => true;

        public abstract void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb);
        public int Offset => 0;
        int ISortableSymbolNode.ClassCode => ClassCode;

        protected override string GetName(NodeFactory factory) => this.GetMangledName(factory.NameMangler);

        int ISortableSymbolNode.CompareToImpl(ISortableSymbolNode other, CompilerComparer comparer)
        {
            return CompareToImpl((SortableDependencyNode)other, comparer);
        }
    }

    public class ReadyToRunHelperSignature : ReadyToRunSignature
    {
        private ReadyToRunHelper _helper;

        public ReadyToRunHelperSignature(ReadyToRunHelper helper)
        {
            _helper = helper;
        }

        protected override int ClassCode => 208107954;

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            ObjectDataBuilder dataBuilder = new ObjectDataBuilder();
            dataBuilder.AddSymbol(this);

            dataBuilder.EmitByte((byte)ReadyToRunFixupKind.READYTORUN_FIXUP_Helper);
            dataBuilder.EmitByte((byte)_helper);

            return dataBuilder.ToObjectData();
        }

        public override void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix);
            sb.Append("ReadyToRunHelper_");
            sb.Append(_helper.ToString());
        }

        protected override int CompareToImpl(SortableDependencyNode other, CompilerComparer comparer)
        {
            return _helper.CompareTo(((ReadyToRunHelperSignature)other)._helper);
        }
    }

    public abstract class ReadyToRunImport : EmbeddedObjectNode
    {
        public abstract EmbeddedPointerIndirectionNode<ReadyToRunSignature> GetSignature(NodeFactory factory);
    }

    public class ReadyToRunModuleImport : ReadyToRunImport
    {
        EmbeddedPointerIndirectionNode<ReadyToRunSignature> _signature;
        
        public ReadyToRunModuleImport()
        {
            _signature = new RvaEmbeddedPointerIndirectionNode<ReadyToRunSignature>(new ReadyToRunHelperSignature(ReadyToRunHelper.READYTORUN_HELPER_Module));
        }
        public override bool StaticDependenciesAreComputed => true;

        protected override int ClassCode => throw new NotImplementedException();

        public override void EncodeData(ref ObjectDataBuilder dataBuilder, NodeFactory factory, bool relocsOnly)
        {
            // This needs to be an empty target pointer since it will be filled in with Module*
            // when loaded by CoreCLR
            dataBuilder.EmitZeroPointer();
        }

        public override EmbeddedPointerIndirectionNode<ReadyToRunSignature> GetSignature(NodeFactory factory)
        {
            //
            // Todo: Get this from factory once we decide on the API for making signatures
            //
            return _signature;
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            var signature = GetSignature(factory);
            if (signature != null)
                yield return new DependencyListEntry(signature, "Signature for ready-to-run fixup import");
        }

        protected override string GetName(NodeFactory context)
        {
            return "ModuleImport";
        }
    }

    public class ImportSectionNode : EmbeddedObjectNode
    {
        private ArrayOfEmbeddedDataNode<ReadyToRunImport> _imports = new ArrayOfEmbeddedDataNode<ReadyToRunImport>("aa", "bb", null);
        private ArrayOfEmbeddedPointersNode<ReadyToRunSignature> _signatures = new ArrayOfEmbeddedPointersNode<ReadyToRunSignature>("cc", "dd", null);
        CorCompileImportType _type;
        CorCompileImportFlags _flags;
        byte _entrySize;

        public ImportSectionNode(CorCompileImportType importType, CorCompileImportFlags flags, byte entrySize)
        {
            _type = importType;
            _flags = flags;
            _entrySize = entrySize;
        }

        public void AddImport(NodeFactory factory, ReadyToRunImport import)
        {
            _imports.AddEmbeddedObject(import);

            var signature = import.GetSignature(factory);
            if (signature != null)
            {
                _signatures.AddEmbeddedObject(signature);
            }
        }

        public override bool StaticDependenciesAreComputed => true;

        protected override int ClassCode => -62839441;

        public override void EncodeData(ref ObjectDataBuilder dataBuilder, NodeFactory factory, bool relocsOnly)
        {
            dataBuilder.EmitReloc(_imports.StartSymbol, RelocType.IMAGE_REL_BASED_ADDR32NB, 0);
            if (!relocsOnly)
                dataBuilder.EmitInt(_imports.GetData(factory, false).Data.Length);

            dataBuilder.EmitShort((short)_flags);
            dataBuilder.EmitByte((byte)_type);
            dataBuilder.EmitByte(_entrySize);
            if (!_signatures.ShouldSkipEmittingObjectNode(factory))
            {
                dataBuilder.EmitReloc(_signatures.StartSymbol, RelocType.IMAGE_REL_BASED_ADDR32NB, 0);
            }
            else
            {
                dataBuilder.EmitUInt(0);
            }
            
            // Todo: Auxilliary data
            dataBuilder.EmitUInt(0);
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory context)
        {
            yield return new DependencyListEntry(_imports, "Import section fixup data");
            yield return new DependencyListEntry(_signatures, "Import section signatures");
        }

        protected override string GetName(NodeFactory context)
        {
            throw new NotImplementedException();
        }
    }
}
