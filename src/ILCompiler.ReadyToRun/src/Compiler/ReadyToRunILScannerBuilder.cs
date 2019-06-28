// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

using ILCompiler.DependencyAnalysis;
using ILCompiler.DependencyAnalysisFramework;
using ILCompiler.DependencyAnalysis.ReadyToRun;
using Internal.IL;
using Internal.TypeSystem;

namespace ILCompiler
{
    class ReadyToRunILScannerBuilder : ILScannerBuilder
    {
        string _inputFilePath;

        internal ReadyToRunILScannerBuilder(CompilerTypeSystemContext context, CompilationModuleGroup compilationGroup, NameMangler mangler, ILProvider ilProvider, PInvokeILEmitterConfiguration pinvokePolicy, string inputFilePath)
            : base(context, compilationGroup, mangler, ilProvider, pinvokePolicy)
        {
            _inputFilePath = inputFilePath;
        }

        public override IILScanner ToILScanner()
        {
            var interopStubManager = new CompilerGeneratedInteropStubManager(_compilationGroup, _context, new InteropStateManager(_context.GeneratedAssembly));
            ModuleTokenResolver moduleTokenResolver = new ModuleTokenResolver(_compilationGroup, _context);
            SignatureContext signatureContext = new SignatureContext(_context.GetModuleFromPath(_inputFilePath), moduleTokenResolver);
            var nodeFactory = new ReadyToRunILScanNodeFactory(_context, _compilationGroup, _metadataManager, interopStubManager, _nameMangler, moduleTokenResolver, signatureContext);
            DependencyAnalyzerBase<NodeFactory> graph = _dependencyTrackingLevel.CreateDependencyGraph(nodeFactory);

            return new ReadyToRunILScanner(graph, nodeFactory, _compilationRoots, _ilProvider, new NullDebugInformationProvider(), _pinvokePolicy, _logger);
        }
    }
}
