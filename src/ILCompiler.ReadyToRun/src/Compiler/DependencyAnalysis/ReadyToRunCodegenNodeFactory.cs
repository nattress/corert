// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILCompiler.DependencyAnalysisFramework;

using Internal.JitInterface;
using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

using ILCompiler.DependencyAnalysis.ReadyToRun;

namespace ILCompiler.DependencyAnalysis
{
    using ReadyToRunHelper = ILCompiler.DependencyAnalysis.ReadyToRun.ReadyToRunHelper;
    public struct MethodWithToken
    {
        public readonly MethodDesc Method;
        public readonly mdToken Token;

        public MethodWithToken(MethodDesc method, mdToken token)
        {
            Method = method;
            Token = token;
        }

        public bool Equals(MethodWithToken other) => Method == other.Method && Token == other.Token;
        public override bool Equals(object obj) => obj is MethodWithToken && Equals((MethodWithToken)obj);
        public override int GetHashCode() => Method.GetHashCode();
    }

    public sealed class ReadyToRunCodegenNodeFactory : NodeFactory
    {
        private Dictionary<MethodDesc, IMethodNode> _importMethods;

        private Dictionary<mdToken, ISymbolNode> _importStrings;

        public ReadyToRunCodegenNodeFactory(CompilerTypeSystemContext context, CompilationModuleGroup compilationModuleGroup, MetadataManager metadataManager,
            InteropStubManager interopStubManager, NameMangler nameMangler, VTableSliceProvider vtableSliceProvider, DictionaryLayoutProvider dictionaryLayoutProvider)
            : base(context, 
                  compilationModuleGroup, 
                  metadataManager, 
                  interopStubManager, 
                  nameMangler, 
                  new LazyGenericsDisabledPolicy(), 
                  vtableSliceProvider, 
                  dictionaryLayoutProvider, 
                  new ImportedNodeProviderThrowing())
        {
            _importMethods = new Dictionary<MethodDesc, IMethodNode>();
            _importStrings = new Dictionary<mdToken, ISymbolNode>();
            _methodsWithTokenEntrypoints = new NodeCache<MethodWithToken, IMethodNode>(methodWithToken =>
            {
                if (CompilationModuleGroup.ContainsMethodBody(methodWithToken.Method, false))
                {
                    MethodWithGCInfo newMethodNode = new MethodWithGCInfo(methodWithToken.Method);
                    _runtimeFunctionsGCInfo.AddEmbeddedObject(newMethodNode.GCInfoNode);
                    int methodIndex = RuntimeFunctionsTable.Add(newMethodNode, newMethodNode.GCInfoNode);
                    MethodEntryPointTable.Add(newMethodNode, methodIndex, this);
                    
                    // TODO: hack - how do we distinguish emitting main entry point from calls between
                    // methods?
                    if (token == 0)
                    {
                        return newMethodNode;
                    }

                    return GetOrAddImportedMethodNode(method, unboxingStub: false, token: token, localMethod: newMethodNode);
                }
                else
                {
                    return GetOrAddImportedMethodNode(method, unboxingStub: false, token: token, localMethod: null);
                }
            });
        }

        public PEReader PEReader;

        public HeaderNode Header;

        public RuntimeFunctionsTableNode RuntimeFunctionsTable;

        private RuntimeFunctionsGCInfoNode _runtimeFunctionsGCInfo;

        public MethodEntryPointTableNode MethodEntryPointTable;

        public InstanceEntryPointTableNode InstanceEntryPointTable;

        public TypesTableNode TypesTable;

        public ImportSectionsTableNode ImportSectionsTable;

        public Import ModuleImport;

        public ImportSectionNode EagerImports;

        public ImportSectionNode MethodImports;

        public ImportSectionNode StringImports;

        public ImportSectionNode HelperImports;

        protected override IMethodNode CreateMethodEntrypointNode(MethodDesc method)
        {
            return _methodsWithTokenEntrypoints.GetOrAdd(new MethodWithToken(method, default(mdToken)));
        }

        private NodeCache<MethodWithToken, IMethodNode> _methodsWithTokenEntrypoints;

        public IMethodNode MethodEntrypointWithToken(MethodDesc method, mdToken token)
        {
            return _methodsWithTokenEntrypoints.GetOrAdd(new MethodWithToken(method, token));
        }

        public override ISymbolNode SerializedStringObject(string data, mdToken token)
        {
            ISymbolNode stringNode;
            if (!_importStrings.TryGetValue(token, out stringNode))
            {
                StringImport r2rImportNode = new StringImport(StringImports, token);
                StringImports.AddImport(this, r2rImportNode);
                stringNode = r2rImportNode;
                _importStrings.Add(token, stringNode);
            }
            return stringNode;
        }

        protected override ISymbolNode CreateReadyToRunHelperNode(ReadyToRunHelperKey helperCall)
        {
            throw new NotImplementedException();
        }

        public override bool CanInline(MethodDesc callerMethod, MethodDesc calleeMethod)
        {
            // By default impose no restrictions on inlining
            return CompilationModuleGroup.ContainsMethodBody(calleeMethod, unboxingStub: false);
        }

        Dictionary<ReadyToRunHelperId, ISymbolNode> _r2rHelpers = new Dictionary<ReadyToRunHelperId, ISymbolNode>();

        public override ISymbolNode ReadyToRunHelperWithToken(ReadyToRunHelperId id, object target, mdToken token)
        {
            ISymbolNode helperNode;
            if (_r2rHelpers.TryGetValue(id, out helperNode))
            {
                return helperNode;
            }

            switch (id)
            {
                case ReadyToRunHelperId.NewHelper:
                    helperNode = CreateNewHelper((TypeDesc)target, token);
                    break;

                case ReadyToRunHelperId.NewArr1:
                    helperNode = CreateNewArrayHelper((ArrayType)target, token);
                    break;

                case ReadyToRunHelperId.GetGCStaticBase:
                    helperNode = CreateGCStaticBaseHelper((TypeDesc)target, token);
                    break;

                case ReadyToRunHelperId.GetNonGCStaticBase:
                    helperNode = CreateNonGCStaticBaseHelper((TypeDesc)target, token);
                    break;

                case ReadyToRunHelperId.GetThreadStaticBase:
                    helperNode = CreateThreadStaticBaseHelper((TypeDesc)target, token);
                    break;

                case ReadyToRunHelperId.IsInstanceOf:
                    helperNode = CreateIsInstanceOfHelper((TypeDesc)target, token);
                    break;

                case ReadyToRunHelperId.CastClass:
                    helperNode = CreateCastClassHelper((TypeDesc)target, token);
                    break;

                case ReadyToRunHelperId.TypeHandle:
                case ReadyToRunHelperId.NecessaryTypeHandle:
                    helperNode = CreateTypeHandleHelper((TypeDesc)target, token);
                    break;

                case ReadyToRunHelperId.VirtualCall:
                    helperNode = CreateVirtualCallHelper((MethodDesc)target, token);
                    break;

                default:
                    throw new NotImplementedException();
            }

            _r2rHelpers.Add(id, helperNode);
            return helperNode;
        }

        private ISymbolNode CreateNewHelper(TypeDesc type, mdToken ctorMemberRefOrTypeRefToken)
        {
            MetadataReader mdReader = PEReader.GetMetadataReader();
            mdToken typeRefToken;
            EntityHandle handle = (EntityHandle)MetadataTokens.Handle((int)ctorMemberRefOrTypeRefToken);
            switch (handle.Kind)
            {
                case HandleKind.TypeReference:
                    typeRefToken = ctorMemberRefOrTypeRefToken;
                    break;

                case HandleKind.MemberReference:
                    {
                        MemberReferenceHandle memberRefHandle = (MemberReferenceHandle)handle;
                        MemberReference memberRef = mdReader.GetMemberReference(memberRefHandle);
                        typeRefToken = (mdToken)MetadataTokens.GetToken(memberRef.Parent);
                    }
                    break;

                default:
                    throw new NotImplementedException();
            }

            return new DelayLoadHelper(this, new NewObjectFixupSignature(type, typeRefToken));
        }

        private ISymbolNode CreateNewArrayHelper(ArrayType type, mdToken typeRefToken)
        {
            Debug.Assert(SignatureBuilder.TypeFromToken(typeRefToken) == CorTokenType.mdtTypeRef);
            return new DelayLoadHelper(this, new NewArrayFixupSignature(type, typeRefToken));
        }

        private ISymbolNode CreateGCStaticBaseHelper(TypeDesc type, mdToken token)
        {
            return new DelayLoadHelper(this, new TypeFixupSignature(
                ReadyToRunFixupKind.READYTORUN_FIXUP_StaticBaseGC, type, GetTypeToken(token)));
        }

        private ISymbolNode CreateNonGCStaticBaseHelper(TypeDesc type, mdToken token)
        {
            return new DelayLoadHelper(this, new TypeFixupSignature(
                ReadyToRunFixupKind.READYTORUN_FIXUP_StaticBaseNonGC, type, GetTypeToken(token)));
        }

        private ISymbolNode CreateThreadStaticBaseHelper(TypeDesc type, mdToken token)
        {
            ReadyToRunFixupKind fixupKind = (type.IsValueType
                ? ReadyToRunFixupKind.READYTORUN_FIXUP_ThreadStaticBaseNonGC
                : ReadyToRunFixupKind.READYTORUN_FIXUP_ThreadStaticBaseGC);
            return new DelayLoadHelper(this, new TypeFixupSignature(fixupKind, type, GetTypeToken(token)));
        }

        private mdToken GetTypeToken(mdToken token)
        {
            MetadataReader mdReader = PEReader.GetMetadataReader();
            mdToken typeToken;
            EntityHandle handle = (EntityHandle)MetadataTokens.Handle((int)token);
            switch (handle.Kind)
            {
                case HandleKind.TypeReference:
                case HandleKind.TypeDefinition:
                    typeToken = token;
                    break;

                case HandleKind.MemberReference:
                    {
                        MemberReferenceHandle memberRefHandle = (MemberReferenceHandle)handle;
                        MemberReference memberRef = mdReader.GetMemberReference(memberRefHandle);
                        typeToken = (mdToken)MetadataTokens.GetToken(memberRef.Parent);
                    }
                    break;

                case HandleKind.FieldDefinition:
                    {
                        FieldDefinitionHandle fieldDefHandle = (FieldDefinitionHandle)handle;
                        FieldDefinition fieldDef = mdReader.GetFieldDefinition(fieldDefHandle);
                        typeToken = (mdToken)MetadataTokens.GetToken(fieldDef.GetDeclaringType());
                    }
                    break;

                case HandleKind.MethodDefinition:
                    {
                        MethodDefinitionHandle methodDefHandle = (MethodDefinitionHandle)handle;
                        MethodDefinition methodDef = mdReader.GetMethodDefinition(methodDefHandle);
                        typeToken = (mdToken)MetadataTokens.GetToken(methodDef.GetDeclaringType());
                    }
                    break;

                default:
                    throw new NotImplementedException();
            }

            return typeToken;
        }

        private ISymbolNode CreateIsInstanceOfHelper(TypeDesc type, mdToken typeRefToken)
        {
            return new DelayLoadHelper(this, new TypeFixupSignature(
                ReadyToRunFixupKind.READYTORUN_FIXUP_IsInstanceOf, type, typeRefToken));
        }

        private ISymbolNode CreateCastClassHelper(TypeDesc type, mdToken typeRefToken)
        {
            return new DelayLoadHelper(this, new TypeFixupSignature(
                ReadyToRunFixupKind.READYTORUN_FIXUP_ChkCast, type, typeRefToken));
        }

        private ISymbolNode CreateTypeHandleHelper(TypeDesc type, mdToken typeRefToken)
        {
            return new DelayLoadHelper(this, new TypeFixupSignature(
                ReadyToRunFixupKind.READYTORUN_FIXUP_TypeHandle, type, typeRefToken));
        }

        private ISymbolNode CreateVirtualCallHelper(MethodDesc method, mdToken methodRefToken)
        {
            return new DelayLoadHelper(this, new MethodFixupSignature(
                ReadyToRunFixupKind.READYTORUN_FIXUP_VirtualEntry_RefToken, method, methodRefToken));
        }

        Dictionary<ILCompiler.ReadyToRunHelper, ISymbolNode> _helperCache = new Dictionary<ILCompiler.ReadyToRunHelper, ISymbolNode>();

        public override ISymbolNode ExternSymbol(ILCompiler.ReadyToRunHelper helper, string name)
        {
            ISymbolNode result;
            if (_helperCache.TryGetValue(helper, out result))
            {
                return result;
            }

            switch (helper)
            {
                case ILCompiler.ReadyToRunHelper.Box:
                    result = CreateBoxHelper();
                    break;

                case ILCompiler.ReadyToRunHelper.Box_Nullable:
                    result = CreateBoxNullableHelper();
                    break;

                case ILCompiler.ReadyToRunHelper.Unbox:
                    result = CreateUnboxHelper();
                    break;

                case ILCompiler.ReadyToRunHelper.Unbox_Nullable:
                    result = CreateUnboxNullableHelper();
                    break;

                case ILCompiler.ReadyToRunHelper.GetRuntimeTypeHandle:
                    return CreateGetRuntimeTypeHandleHelper();

                case ILCompiler.ReadyToRunHelper.RngChkFail:
                    return CreateRangeCheckFailureHelper();

                default:
                    throw new NotImplementedException();
            }

            _helperCache.Add(helper, result);
            return result;
        }

        public override ISymbolNode HelperMethodEntrypoint(ILCompiler.ReadyToRunHelper helperId, MethodDesc method)
        {
            return ExternSymbol(helperId, null);
        }


        private ISymbolNode CreateBoxHelper()
        {
            return GetReadyToRunHelperCell(ILCompiler.DependencyAnalysis.ReadyToRun.ReadyToRunHelper.READYTORUN_HELPER_Box);
        }

        private ISymbolNode CreateBoxNullableHelper()
        {
            return GetReadyToRunHelperCell(ILCompiler.DependencyAnalysis.ReadyToRun.ReadyToRunHelper.READYTORUN_HELPER_Box_Nullable);
        }

        private ISymbolNode CreateUnboxHelper()
        {
            return GetReadyToRunHelperCell(ILCompiler.DependencyAnalysis.ReadyToRun.ReadyToRunHelper.READYTORUN_HELPER_Unbox);
        }

        private ISymbolNode CreateUnboxNullableHelper()
        {
            return GetReadyToRunHelperCell(ILCompiler.DependencyAnalysis.ReadyToRun.ReadyToRunHelper.READYTORUN_HELPER_Unbox_Nullable);
        }

        private ISymbolNode CreateGetRuntimeTypeHandleHelper()
        {
            return GetReadyToRunHelperCell(ILCompiler.DependencyAnalysis.ReadyToRun.ReadyToRunHelper.READYTORUN_HELPER_GetRuntimeTypeHandle);
        }

        private ISymbolNode CreateRangeCheckFailureHelper()
        {
            return GetReadyToRunHelperCell(ILCompiler.DependencyAnalysis.ReadyToRun.ReadyToRunHelper.READYTORUN_HELPER_RngChkFail);
        }

        protected override IMethodNode CreateUnboxingStubNode(MethodDesc method)
        {
            throw new NotImplementedException();
        }

        private Dictionary<ReadyToRunHelper, ISymbolNode> _constructedHelpers = new Dictionary<ReadyToRunHelper, ISymbolNode>();

        public ISymbolNode GetReadyToRunHelperCell(ReadyToRunHelper helperId)
        {
            ISymbolNode helperCell;
            if (!_constructedHelpers.TryGetValue(helperId, out helperCell))
            {
                helperCell = CreateReadyToRunHelperCell(helperId);
                _constructedHelpers.Add(helperId, helperCell);
            }
            return helperCell;
        }

        private ISymbolNode CreateReadyToRunHelperCell(ReadyToRunHelper helperId)
        {
            Import helperCell = new Import(EagerImports, new ReadyToRunHelperSignature(helperId));
            EagerImports.AddImport(this, helperCell);
            return helperCell;
        }

        public override ISymbolNode ComputeConstantLookup(Compilation compilation, ReadyToRunHelperId helperId, object entity, mdToken token)
        {
            return ReadyToRunHelperWithToken(helperId, entity, token);
        }

        public override void AttachToDependencyGraph(DependencyAnalyzerBase<NodeFactory> graph)
        {
            Header = new HeaderNode(Target);

            var compilerIdentifierNode = new CompilerIdentifierNode(Target);
            Header.Add(Internal.Runtime.ReadyToRunSectionType.CompilerIdentifier, compilerIdentifierNode, compilerIdentifierNode);

            RuntimeFunctionsTable = new RuntimeFunctionsTableNode(Target);
            Header.Add(Internal.Runtime.ReadyToRunSectionType.RuntimeFunctions, RuntimeFunctionsTable, RuntimeFunctionsTable);

            _runtimeFunctionsGCInfo = new RuntimeFunctionsGCInfoNode();
            graph.AddRoot(_runtimeFunctionsGCInfo, "GC info is always generated");

            MethodEntryPointTable = new MethodEntryPointTableNode(Target);
            Header.Add(Internal.Runtime.ReadyToRunSectionType.MethodDefEntryPoints, MethodEntryPointTable, MethodEntryPointTable);

            InstanceEntryPointTable = new InstanceEntryPointTableNode(Target);
            Header.Add(Internal.Runtime.ReadyToRunSectionType.InstanceMethodEntryPoints, InstanceEntryPointTable, InstanceEntryPointTable);

            TypesTable = new TypesTableNode(Target);
            Header.Add(Internal.Runtime.ReadyToRunSectionType.AvailableTypes, TypesTable, TypesTable);

            ImportSectionsTable = new ImportSectionsTableNode(Target);
            Header.Add(Internal.Runtime.ReadyToRunSectionType.ImportSections, ImportSectionsTable, ImportSectionsTable.StartSymbol);

            EagerImports = new ImportSectionNode(
                "EagerImports", 
                CorCompileImportType.CORCOMPILE_IMPORT_TYPE_UNKNOWN, 
                CorCompileImportFlags.CORCOMPILE_IMPORT_FLAGS_EAGER,
                (byte)Target.PointerSize);
            ImportSectionsTable.AddEmbeddedObject(EagerImports);

            // All ready-to-run images have a module import helper which gets patched by the runtime on image load
            ModuleImport = new Import(EagerImports, new ReadyToRunHelperSignature(
                ILCompiler.DependencyAnalysis.ReadyToRun.ReadyToRunHelper.READYTORUN_HELPER_Module));
            EagerImports.AddImport(this, ModuleImport);

            MethodImports = new ImportSectionNode(
                "MethodImports",
                CorCompileImportType.CORCOMPILE_IMPORT_TYPE_STUB_DISPATCH,
                CorCompileImportFlags.CORCOMPILE_IMPORT_FLAGS_PCODE,
                (byte)Target.PointerSize);
            ImportSectionsTable.AddEmbeddedObject(MethodImports);

            HelperImports = new ImportSectionNode(
                "HelperImports",
                CorCompileImportType.CORCOMPILE_IMPORT_TYPE_UNKNOWN,
                CorCompileImportFlags.CORCOMPILE_IMPORT_FLAGS_PCODE,
                (byte)Target.PointerSize);
            ImportSectionsTable.AddEmbeddedObject(HelperImports);

            StringImports = new ImportSectionNode(
                "StringImports",
                CorCompileImportType.CORCOMPILE_IMPORT_TYPE_STRING_HANDLE,
                CorCompileImportFlags.CORCOMPILE_IMPORT_FLAGS_UNKNOWN,
                (byte)Target.PointerSize);
            ImportSectionsTable.AddEmbeddedObject(StringImports);

            graph.AddRoot(ModuleImport, "Module import is always generated");
            graph.AddRoot(EagerImports, "Eager imports are always generated");
            graph.AddRoot(MethodImports, "Method imports are always generated");
            graph.AddRoot(HelperImports, "Helper imports are always generated");
            graph.AddRoot(StringImports, "String imports are always generated");
            graph.AddRoot(ImportSectionsTable, "Import sections table is always generated");
            graph.AddRoot(Header, "ReadyToRunHeader is always generated");
        }

        public IMethodNode GetOrAddImportedMethodNode(MethodDesc method, bool unboxingStub, mdToken token, MethodWithGCInfo localMethod)
        {
            CorTokenType tokenType = SignatureBuilder.TypeFromToken(token);
            ReadyToRunFixupKind fixupKind;
            switch (tokenType)
            {
                case CorTokenType.mdtMethodDef:
                    fixupKind = ReadyToRunFixupKind.READYTORUN_FIXUP_MethodEntry_DefToken;
                    break;

                case CorTokenType.mdtMemberRef:
                    fixupKind = ReadyToRunFixupKind.READYTORUN_FIXUP_MethodEntry_RefToken;
                    break;

                default:
                    throw new NotImplementedException();
            }

            Debug.Assert(tokenType == CorTokenType.mdtMemberRef || tokenType == CorTokenType.mdtMethodDef);
            IMethodNode methodImport;
            if (!_importMethods.TryGetValue(method, out methodImport))
            {
                // First time we see a given external method - emit indirection cell and the import entry
                ReadyToRun.MethodImport indirectionCell = new ReadyToRun.MethodImport(MethodImports, fixupKind, method, token, localMethod);
                MethodImports.AddImport(this, indirectionCell);
                _importMethods.Add(method, indirectionCell);
                methodImport = indirectionCell;
            }
            return methodImport;
        }

    }
}
