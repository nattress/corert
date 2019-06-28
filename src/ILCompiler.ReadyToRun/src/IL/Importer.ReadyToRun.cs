// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

using ILCompiler;
using ILCompiler.DependencyAnalysis;
using Internal.TypeSystem;
using Internal.IL;

using Debug = System.Diagnostics.Debug;
using DependencyList = ILCompiler.DependencyAnalysisFramework.DependencyNodeCore<ILCompiler.DependencyAnalysis.NodeFactory>.DependencyList;

namespace Internal.IL
{
    internal partial class ILImporter
    {
        private readonly MethodIL _methodIL;
        private readonly MethodIL _canonMethodIL;
        private readonly ReadyToRunILScanner _compilation;
        private readonly ReadyToRunILScanNodeFactory _factory;
        private readonly byte[] _ilBytes;
        private readonly MethodDesc _canonMethod;
        private readonly DependencyList _dependencies = new DependencyList();
        private int _currentInstructionOffset;
        private int _previousInstructionOffset;

        // Use ILToCppImporter's enum flags instead?
        //private bool _isReadOnly;
        private TypeDesc _constrained;

        private class BasicBlock
        {
            // Common fields
            public enum ImportState : byte
            {
                Unmarked,
                IsPending
            }

            public BasicBlock Next;

            public int StartOffset;
            public ImportState State = ImportState.Unmarked;

            public bool TryStart;
            public bool FilterStart;
            public bool HandlerStart;
        }

        private class ExceptionRegion
        {
            public ILExceptionRegion ILRegion;
        };

        private ExceptionRegion[] _exceptionRegions;

        public ILImporter(ReadyToRunILScanner compilation, MethodDesc method)
        {
            var methodIL = compilation.GetMethodIL(method);
            
            _compilation = compilation;
            _factory = (ReadyToRunILScanNodeFactory)compilation.NodeFactory;

            _ilBytes = methodIL.GetILBytes();

            _canonMethodIL = methodIL;

            // Get the runtime determined method IL so that this works right in shared code
            // and tokens in shared code resolve to runtime determined types.
            MethodIL uninstantiatiedMethodIL = methodIL.GetMethodILDefinition();
            if (methodIL != uninstantiatiedMethodIL)
            {
                MethodDesc sharedMethod = method.GetSharedRuntimeFormMethodTarget();
                _methodIL = new InstantiatedMethodIL(sharedMethod, uninstantiatiedMethodIL);
            }
            else
            {
                _methodIL = methodIL;
            }

            _canonMethod = method;

            var ilExceptionRegions = methodIL.GetExceptionRegions();
            _exceptionRegions = new ExceptionRegion[ilExceptionRegions.Length];
            for (int i = 0; i < ilExceptionRegions.Length; i++)
            {
                _exceptionRegions[i] = new ExceptionRegion() { ILRegion = ilExceptionRegions[i] };
            }
        }

        public DependencyList Import()
        {
            FindBasicBlocks();
            ImportBasicBlocks();

            return _dependencies;
        }

        private void MarkInstructionBoundary() { }
        private void EndImportingBasicBlock(BasicBlock basicBlock) { }

        private void StartImportingBasicBlock(BasicBlock basicBlock)
        {
            // Import all associated EH regions
            foreach (ExceptionRegion ehRegion in _exceptionRegions)
            {
                ILExceptionRegion region = ehRegion.ILRegion;
                if (region.TryOffset == basicBlock.StartOffset)
                {
                    MarkBasicBlock(_basicBlocks[region.HandlerOffset]);
                    if (region.Kind == ILExceptionRegionKind.Filter)
                        MarkBasicBlock(_basicBlocks[region.FilterOffset]);

                    // Once https://github.com/dotnet/corert/issues/3460 is done, this should be deleted.
                    // Throwing InvalidProgram is not great, but we want to do *something* if this happens
                    // because doing nothing means problems at runtime. This is not worth piping a
                    // a new exception with a fancy message for.
                    if (region.Kind == ILExceptionRegionKind.Catch)
                    {
                        TypeDesc catchType = (TypeDesc)_methodIL.GetObject(region.ClassToken);
                        if (catchType.IsRuntimeDeterminedSubtype)
                            ThrowHelper.ThrowInvalidProgramException();
                    }
                }
            }

            _currentInstructionOffset = -1;
            _previousInstructionOffset = -1;
        }

        private void StartImportingInstruction()
        {
            _previousInstructionOffset = _currentInstructionOffset;
            _currentInstructionOffset = _currentOffset;
        }

        private void EndImportingInstruction()
        {
            // The instruction should have consumed any prefixes.
            _constrained = null;
            //_isReadOnly = false;
        }

        private void ImportJmp(int token)
        {
            // JMP is kind of like a tail call (with no arguments pushed on the stack).
            ImportCall(ILOpcode.call, token);
        }

        private void ImportCasting(ILOpcode opcode, int token)
        {
            //TypeDesc type = (TypeDesc)_methodIL.GetObject(token);

            //if (type.IsRuntimeDeterminedSubtype)
            //{
            //    _dependencies.Add(GetGenericLookupHelper(ReadyToRunHelperId.TypeHandleForCasting, type), "IsInst/CastClass");
            //}
            //else
            //{
            //    _dependencies.Add(_compilation.ComputeConstantLookup(ReadyToRunHelperId.TypeHandleForCasting, type), "IsInst/CastClass");
            //}
        }

        private void ImportCall(ILOpcode opcode, int token)
        {
            
        }

        private void ImportLdFtn(int token, ILOpcode opCode)
        {
            // Is this a verifiable delegate creation? If so, we will handle it when we reach the newobj
            if (_ilBytes[_currentOffset] == (byte)ILOpcode.newobj)
            {
                int delegateToken = ReadILTokenAt(_currentOffset + 1);
                var delegateType = ((MethodDesc)_methodIL.GetObject(delegateToken)).OwningType;
                if (delegateType.IsDelegate)
                    return;
            }

            ImportCall(opCode, token);
        }

        private void ImportBranch(ILOpcode opcode, BasicBlock target, BasicBlock fallthrough)
        {
            ImportFallthrough(target);

            if (fallthrough != null)
                ImportFallthrough(fallthrough);
        }

        private void ImportSwitchJump(int jmpBase, int[] jmpDelta, BasicBlock fallthrough)
        {
            for (int i = 0; i < jmpDelta.Length; i++)
            {
                BasicBlock target = _basicBlocks[jmpBase + jmpDelta[i]];
                ImportFallthrough(target);
            }

            if (fallthrough != null)
                ImportFallthrough(fallthrough);
        }

        private void ImportUnbox(int token, ILOpcode opCode)
        {
            // Lots in Scanner implementation
        }

        private void ImportRefAnyVal(int token)
        {
            //_dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.GetRefAny), "refanyval");
        }

        private void ImportMkRefAny(int token)
        {
            //_dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.TypeHandleToRuntimeType), "mkrefany");
            //_dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.TypeHandleToRuntimeTypeHandle), "mkrefany");
        }

        private void ImportLdToken(int token)
        {
            // Lots in Scanner implementation
        }

        private void ImportRefAnyType()
        {
            // TODO
        }

        private void ImportArgList()
        {
        }

        private void ImportConstrainedPrefix(int token)
        {
            _constrained = (TypeDesc)_methodIL.GetObject(token);
        }

        private void ImportReadOnlyPrefix()
        {
            //_isReadOnly = true;
        }

        private void ImportFieldAccess(int token, bool isStatic, string reason)
        {
            // Lots in Scanner implementation
        }

        private void ImportLoadField(int token, bool isStatic)
        {
            ImportFieldAccess(token, isStatic, isStatic ? "ldsfld" : "ldfld");
        }

        private void ImportAddressOfField(int token, bool isStatic)
        {
            ImportFieldAccess(token, isStatic, isStatic ? "ldsflda" : "ldflda");
        }

        private void ImportStoreField(int token, bool isStatic)
        {
            ImportFieldAccess(token, isStatic, isStatic ? "stsfld" : "stfld");
        }

        private void ImportLoadString(int token)
        {
            // If we care, this can include allocating the frozen string node.
            //_dependencies.Add(_factory.SerializedStringObject(""), "ldstr");
        }

        private void ImportBox(int token)
        {
            //AddBoxingDependencies((TypeDesc)_methodIL.GetObject(token), "Box");
        }

        private void ImportLeave(BasicBlock target)
        {
            ImportFallthrough(target);
        }

        private void ImportNewArray(int token)
        {
            // Lots in Scanner implementation
        }

        private void ImportLoadElement(int token)
        {
            //_dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.RngChkFail), "ldelem");
        }

        private void ImportLoadElement(TypeDesc elementType)
        {
            //_dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.RngChkFail), "ldelem");
        }

        private void ImportStoreElement(int token)
        {
            //_dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.RngChkFail), "stelem");
        }

        private void ImportStoreElement(TypeDesc elementType)
        {
            //_dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.RngChkFail), "stelem");
        }

        private void ImportAddressOfElement(int token)
        {
            // Lots in Scanner implementation
        }

        private void ImportBinaryOperation(ILOpcode opcode)
        {
            //switch (opcode)
            //{
            //    case ILOpcode.add_ovf:
            //    case ILOpcode.add_ovf_un:
            //    case ILOpcode.mul_ovf:
            //    case ILOpcode.mul_ovf_un:
            //    case ILOpcode.sub_ovf:
            //    case ILOpcode.sub_ovf_un:
            //        _dependencies.Add(GetHelperEntrypoint(ReadyToRunHelper.Overflow), "_ovf");
            //        break;
            //}
        }

        private void ImportFallthrough(BasicBlock next)
        {
            MarkBasicBlock(next);
        }

        private int ReadILTokenAt(int ilOffset)
        {
            return (int)(_ilBytes[ilOffset]
                + (_ilBytes[ilOffset + 1] << 8)
                + (_ilBytes[ilOffset + 2] << 16)
                + (_ilBytes[ilOffset + 3] << 24));
        }

        private void ReportInvalidBranchTarget(int targetOffset)
        {
            ThrowHelper.ThrowInvalidProgramException();
        }

        private void ReportFallthroughAtEndOfMethod()
        {
            ThrowHelper.ThrowInvalidProgramException();
        }

        private void ReportMethodEndInsideInstruction()
        {
            ThrowHelper.ThrowInvalidProgramException();
        }

        private void ReportInvalidInstruction(ILOpcode opcode)
        {
            ThrowHelper.ThrowInvalidProgramException();
        }

        private TypeDesc GetWellKnownType(WellKnownType wellKnownType)
        {
            return _compilation.TypeSystemContext.GetWellKnownType(wellKnownType);
        }


        private void ImportNop() { }
        private void ImportBreak() { }
        private void ImportLoadVar(int index, bool argument) { }
        private void ImportStoreVar(int index, bool argument) { }
        private void ImportAddressOfVar(int index, bool argument) { }
        private void ImportDup() { }
        private void ImportPop() { }
        private void ImportCalli(int token) { }
        private void ImportLoadNull() { }
        private void ImportReturn() { }
        private void ImportLoadInt(long value, StackValueKind kind) { }
        private void ImportLoadFloat(double value) { }
        private void ImportLoadIndirect(int token) { }
        private void ImportLoadIndirect(TypeDesc type) { }
        private void ImportStoreIndirect(int token) { }
        private void ImportStoreIndirect(TypeDesc type) { }
        private void ImportShiftOperation(ILOpcode opcode) { }
        private void ImportCompareOperation(ILOpcode opcode) { }
        private void ImportConvert(WellKnownType wellKnownType, bool checkOverflow, bool unsigned) { }
        private void ImportUnaryOperation(ILOpcode opCode) { }
        private void ImportCpOpj(int token) { }
        private void ImportCkFinite() { }
        private void ImportLocalAlloc() { }
        private void ImportEndFilter() { }
        private void ImportCpBlk() { }
        private void ImportInitBlk() { }
        private void ImportRethrow() { }
        private void ImportSizeOf(int token) { }
        private void ImportUnalignedPrefix(byte alignment) { }
        private void ImportVolatilePrefix() { }
        private void ImportTailPrefix() { }
        private void ImportNoPrefix(byte mask) { }
        private void ImportThrow() { }
        private void ImportInitObj(int token) { }
        private void ImportLoadLength() { }
        private void ImportEndFinally() { }
    }
}
