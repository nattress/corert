// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

using ILCompiler.DependencyAnalysis.ReadyToRun;
using Internal.JitInterface;
using Internal.TypeSystem;

namespace ILCompiler.DependencyAnalysis
{
    public sealed class ReadyToRunILScanNodeFactory : ReadyToRunCodegenNodeFactory
    {
        public ReadyToRunILScanNodeFactory(
            CompilerTypeSystemContext context,
            CompilationModuleGroup compilationModuleGroup, 
            MetadataManager metadataManager, 
            InteropStubManager interopStubManager, 
            NameMangler nameMangler, 
            ModuleTokenResolver moduleTokenResolver, 
            SignatureContext signatureContext)
            : base(context, compilationModuleGroup, metadataManager, interopStubManager, nameMangler, new LazyVTableSliceProvider(), new LazyDictionaryLayoutProvider(), moduleTokenResolver, signatureContext)
        {
        }

        
    }
}
