// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.


using Internal.JitInterface;
using Internal.Text;
using Internal.TypeSystem;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    public class MethodFixupSignature : Signature
    {
        private readonly ReadyToRunFixupKind _fixupKind;

        private readonly MethodDesc _methodDesc;
        
        private readonly mdToken _methodRefToken;

        public MethodFixupSignature(ReadyToRunFixupKind fixupKind, MethodDesc methodDesc, mdToken methodRefToken)
        {
            _fixupKind = fixupKind;
            _methodDesc = methodDesc;
            _methodRefToken = methodRefToken;
        }

        protected override int ClassCode => 150063499;

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            ReadyToRunCodegenNodeFactory r2rFactory = (ReadyToRunCodegenNodeFactory)factory;
            ObjectDataBuilder dataBuilder = new ObjectDataBuilder();
            dataBuilder.AddSymbol(this);

            dataBuilder.EmitByte((byte)_fixupKind);
            SignatureBuilder.EmitTokenRid(ref dataBuilder, _methodRefToken);

            return dataBuilder.ToObjectData();
        }

        public override void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix);
            sb.Append($@"MethodFixupSignature({_fixupKind.ToString()}): {_methodDesc.ToString()}; token: {(uint)_methodRefToken:X8})");
        }

        protected override int CompareToImpl(SortableDependencyNode other, CompilerComparer comparer)
        {
            return _methodRefToken.CompareTo(((MethodFixupSignature)other)._methodRefToken);
        }
    }
}
