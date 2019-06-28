// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

using ILCompiler.DependencyAnalysis;
using ILCompiler.DependencyAnalysisFramework;

using Internal.TypeSystem;
using Internal.IL;

namespace ILCompiler
{
    public class ILScannerBuilder
    {
        protected readonly CompilerTypeSystemContext _context;
        protected readonly CompilationModuleGroup _compilationGroup;
        protected readonly NameMangler _nameMangler;
        protected readonly ILProvider _ilProvider;
        protected readonly PInvokeILEmitterConfiguration _pinvokePolicy;

        // These need to provide reasonable defaults so that the user can optionally skip
        // calling the Use/Configure methods and still get something reasonable back.
        protected Logger _logger = Logger.Null;
        protected DependencyTrackingLevel _dependencyTrackingLevel = DependencyTrackingLevel.None;
        protected IEnumerable<ICompilationRootProvider> _compilationRoots = Array.Empty<ICompilationRootProvider>();
        protected MetadataManager _metadataManager;

        protected internal ILScannerBuilder(CompilerTypeSystemContext context, CompilationModuleGroup compilationGroup, NameMangler mangler, ILProvider ilProvider, PInvokeILEmitterConfiguration pinvokePolicy)
        {
            _context = context;
            _compilationGroup = compilationGroup;
            _nameMangler = mangler;
            _metadataManager = new EmptyMetadataManager(context);
            _ilProvider = ilProvider;
            _pinvokePolicy = pinvokePolicy;
        }

        public ILScannerBuilder UseDependencyTracking(DependencyTrackingLevel trackingLevel)
        {
            _dependencyTrackingLevel = trackingLevel;
            return this;
        }

        public ILScannerBuilder UseCompilationRoots(IEnumerable<ICompilationRootProvider> compilationRoots)
        {
            _compilationRoots = compilationRoots;
            return this;
        }

        public ILScannerBuilder UseMetadataManager(MetadataManager metadataManager)
        {
            _metadataManager = metadataManager;
            return this;
        }

        public virtual IILScanner ToILScanner()
        {
            var interopStubManager = new CompilerGeneratedInteropStubManager(_compilationGroup, _context, new InteropStateManager(_context.GeneratedAssembly));
            var nodeFactory = new ILScanNodeFactory(_context, _compilationGroup, _metadataManager, interopStubManager, _nameMangler);
            DependencyAnalyzerBase<NodeFactory> graph = _dependencyTrackingLevel.CreateDependencyGraph(nodeFactory);

            return new ILScanner(graph, nodeFactory, _compilationRoots, _ilProvider, new NullDebugInformationProvider(), _pinvokePolicy, _logger);
        }
    }
}
