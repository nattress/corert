// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

using ILCompiler.DependencyAnalysis;
using ILCompiler.DependencyAnalysisFramework;

using Internal.IL;
using Internal.IL.Stubs;
using Internal.TypeSystem;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler
{
    /// <summary>
    /// IL scan analyzer of programs - this class analyzes what methods, types and other runtime artifact
    /// will need to be generated during a compilation. The result of analysis is a conservative superset of
    /// what methods will be compiled by the actual codegen backend.
    /// </summary>
    internal sealed class ReadyToRunILScanner : Compilation, IILScanner
    {
        internal ReadyToRunILScanner(
            DependencyAnalyzerBase<NodeFactory> dependencyGraph,
            ReadyToRunILScanNodeFactory nodeFactory,
            IEnumerable<ICompilationRootProvider> roots,
            ILProvider ilProvider,
            DebugInformationProvider debugInformationProvider,
            PInvokeILEmitterConfiguration pinvokePolicy,
            Logger logger)
            : base(dependencyGraph, nodeFactory, roots, ilProvider, debugInformationProvider, null, pinvokePolicy, logger)
        {
        }

        protected override void CompileInternal(string outputFile, ObjectDumper dumper)
        {
            // TODO: We should have a base class for compilation that doesn't implement ICompilation so that
            // we don't need this.
            throw new NotSupportedException();
        }

        protected override void ComputeDependencyNodeDependencies(List<DependencyNodeCore<NodeFactory>> obj)
        {
            foreach (DependencyNodeCore<NodeFactory> dependency in obj)
            {
                var methodCodeNodeNeedingCode = dependency as ScannedMethodNode;
                Debug.Assert(methodCodeNodeNeedingCode != null);
                if (methodCodeNodeNeedingCode == null)
                {
                    // To compute dependencies of the shadow method that tracks dictionary
                    // dependencies we need to ensure there is code for the canonical method body.
                    var dependencyMethod = (ShadowConcreteMethodNode)dependency;
                    methodCodeNodeNeedingCode = (ScannedMethodNode)dependencyMethod.CanonicalMethodNode;
                }

                // We might have already compiled this method.
                if (methodCodeNodeNeedingCode.StaticDependenciesAreComputed)
                    continue;

                MethodDesc method = methodCodeNodeNeedingCode.Method;

                //try
                {
                    var importer = new ILImporter(this, method);
                    methodCodeNodeNeedingCode.InitializeDependencies(_nodeFactory, importer.Import());
                }
                // TODO: Re-enable this before checkin. I don't want to silently eat exceptions yet.
                //catch (TypeSystemException)
                {
                    // Compilation failures can be ignored here since there are runtime-determined code patterns
                    // that we cannot compile with ReadyToRun. At actual compile time, the method will be skipped
                    // or it will be fatal and the compiler will be brought down.
                }
            }
        }

        CompilationResults IILScanner.Scan()
        {
            _dependencyGraph.ComputeMarkedNodes();

            return new CompilationResults(_dependencyGraph, _nodeFactory);
        }
    }
}
