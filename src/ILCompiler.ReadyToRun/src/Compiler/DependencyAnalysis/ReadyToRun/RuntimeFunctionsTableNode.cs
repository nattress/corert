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
    public class RuntimeFunctionsTableNode : HeaderTableNode
    {
        private readonly List<(MethodCodeNode Method, ISymbolNode GCInfo)> _methodNodes;

        public RuntimeFunctionsTableNode(TargetDetails target)
            : base(target)
        {
            _methodNodes = new List<(MethodCodeNode Method, ISymbolNode GCInfo)>();
        }

        public override void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix);
            sb.Append("__ReadyToRunRuntimeFunctionsTable");
        }

        public int Add(MethodCodeNode method, ISymbolNode gcInfoNode)
        {
            _methodNodes.Add((Method: method, GCInfo: gcInfoNode));
            return _methodNodes.Count - 1;
        }

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            ObjectDataBuilder runtimeFunctionsBuilder = new ObjectDataBuilder(factory, relocsOnly);

            // Add the symbol representing this object node
            runtimeFunctionsBuilder.AddSymbol(this);

            foreach ((MethodCodeNode Method, ISymbolNode GCInfo) methodAndGCInfo in _methodNodes)
            {
                // StartOffset of the runtime function
                runtimeFunctionsBuilder.EmitReloc(methodAndGCInfo.Method, RelocType.IMAGE_REL_BASED_ADDR32NB, delta: 0);
                if (!relocsOnly && Target.Architecture == TargetArchitecture.X64)
                {
                    // On Amd64, the 2nd word contains the EndOffset of the runtime function
                    int methodLength = methodAndGCInfo.Method.GetData(factory, relocsOnly).Data.Length;
                    runtimeFunctionsBuilder.EmitReloc(methodAndGCInfo.Method, RelocType.IMAGE_REL_BASED_ADDR32NB, delta: methodLength);
                }
                // Emit the GC info RVA
                runtimeFunctionsBuilder.EmitReloc(methodAndGCInfo.GCInfo, RelocType.IMAGE_REL_BASED_ADDR32NB, delta: methodAndGCInfo.GCInfo.Offset);
            }

            return runtimeFunctionsBuilder.ToObjectData();
        }

        protected override int ClassCode => -855231428;
    }
}
