// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.


using Internal.JitInterface;
using Internal.Text;
using Internal.TypeSystem;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    public class NewArrayFixupSignature : Signature
    {
        private readonly ArrayType _arrayType;
        private readonly mdToken _typeToken;

        public NewArrayFixupSignature(ArrayType arrayType, mdToken typeToken)
        {
            _arrayType = arrayType;
            _typeToken = typeToken;
        }

        protected override int ClassCode => 815543321;

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            ReadyToRunCodegenNodeFactory r2rFactory = (ReadyToRunCodegenNodeFactory)factory;
            ObjectDataBuilder dataBuilder = new ObjectDataBuilder();
            dataBuilder.AddSymbol(this);

            dataBuilder.EmitByte((byte)ReadyToRunFixupKind.READYTORUN_FIXUP_NewArray);
            SignatureBuilder.EmitType(ref dataBuilder, _arrayType, _typeToken);

            return dataBuilder.ToObjectData();
        }

        public override void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix);
            sb.Append($@"NewArraySignature: {_arrayType.ToString()}; token: {(uint)_typeToken:X8})");
        }

        protected override int CompareToImpl(SortableDependencyNode other, CompilerComparer comparer)
        {
            return _typeToken.CompareTo(((NewArrayFixupSignature)other)._typeToken);
        }
    }
}
