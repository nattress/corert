// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;

using Internal.NativeFormat;
using Internal.Runtime;
using Internal.Text;
using Internal.TypeSystem;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    class RuntimeFunctionsGCInfoNode : ObjectNode, ISymbolDefinitionNode
    {
        private readonly BlobBuilder _uniqueGCInfo;

        public RuntimeFunctionsGCInfoNode(BlobBuilder uniqueGCInfo)
        {
            _uniqueGCInfo = uniqueGCInfo;
        }

        protected override string GetName(NodeFactory factory)
        {
            return "RuntimeFunctionsGCInfo";
        }

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append("RuntimeFunctionsGCInfo");
        }

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = true)
        {
            return new ObjectData(
                data: _uniqueGCInfo.ToArray(),
                relocs: null,
                alignment: 1,
                definedSymbols: new ISymbolDefinitionNode[] { this });
        }

        protected override int ClassCode => 316678892;

        public override ObjectNodeSection Section => ObjectNodeSection.ReadOnlyDataSection;

        public override bool StaticDependenciesAreComputed => true;

        public override bool IsShareable => false;

        int ISymbolDefinitionNode.Offset => 0;

        int ISymbolNode.Offset => 0;
    }

    public class RuntimeFunctionsTableNode : HeaderTableNode
    {
        private readonly List<(MethodCodeNode Method, int GCInfoOffset)> _methodNodes;

        private readonly BlobBuilder _uniqueGCInfoBuilder;
        private readonly Dictionary<byte[], int> _uniqueGCInfoOffsets;

        private readonly RuntimeFunctionsGCInfoNode _gcInfoNode;

        public RuntimeFunctionsTableNode(TargetDetails target)
            : base(target)
        {
            _methodNodes = new List<(MethodCodeNode Method, int GCInfoOffset)>();
            _uniqueGCInfoBuilder = new BlobBuilder();
            _uniqueGCInfoOffsets = new Dictionary<byte[], int>(ByteArrayComparer.Instance);

            _gcInfoNode = new RuntimeFunctionsGCInfoNode(_uniqueGCInfoBuilder);
        }

        public override void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix);
            sb.Append("__ReadyToRunRuntimeFunctionsTable");
        }

        public int Add(MethodCodeNode method)
        {
            int methodIndex = _methodNodes.Count;

            byte[] gcInfo = method.GCInfo;
            int gcInfoLocalOffset;
            if (!_uniqueGCInfoOffsets.TryGetValue(gcInfo, out gcInfoLocalOffset))
            {
                gcInfoLocalOffset = _uniqueGCInfoBuilder.Count;
                _uniqueGCInfoBuilder.WriteBytes(gcInfo);
                _uniqueGCInfoOffsets.Add(gcInfo, gcInfoLocalOffset);
            }

            _methodNodes.Add((Method: method, GCInfoOffset: gcInfoLocalOffset));

            return methodIndex;
        }

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            ObjectDataBuilder runtimeFunctionsBuilder = new ObjectDataBuilder(factory, relocsOnly);

            // Add the symbol representing this object node
            runtimeFunctionsBuilder.AddSymbol(this);

            if (relocsOnly)
            {
                // Just make sure to mark the GC info as a dependent node
                runtimeFunctionsBuilder.EmitReloc(_gcInfoNode, RelocType.IMAGE_REL_BASED_ADDR32NB, delta: 0);
            }
            else
            {
                foreach ((MethodCodeNode Method, int GCInfoOffset) methodAndOffset in _methodNodes)
                {
                    // StartOffset of the runtime function
                    runtimeFunctionsBuilder.EmitReloc(methodAndOffset.Method, RelocType.IMAGE_REL_BASED_ADDR32NB, delta: 0);
                    if (Target.Architecture == TargetArchitecture.X64)
                    {
                        // On Amd64, the 2nd word contains the EndOffset of the runtime function
                        int methodLength = methodAndOffset.Method.GetData(factory, relocsOnly).Data.Length;
                        runtimeFunctionsBuilder.EmitReloc(methodAndOffset.Method, RelocType.IMAGE_REL_BASED_ADDR32NB, delta: methodLength);
                    }
                    // Emit the GC info RVA
                    runtimeFunctionsBuilder.EmitReloc(_gcInfoNode, RelocType.IMAGE_REL_BASED_ADDR32NB, delta: methodAndOffset.GCInfoOffset);
                }
            }

            return runtimeFunctionsBuilder.ToObjectData();
        }

        protected override int ClassCode => -855231428;
    }
}
