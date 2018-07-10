// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

using Internal.JitInterface;
using Internal.Text;
using Internal.TypeSystem;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    public class MethodImportSignature : Signature
    {
        private readonly MethodDesc _methodDesc;
        private readonly mdToken _token;

        public MethodImportSignature(MethodDesc methodDesc, mdToken token)
        {
            Debug.Assert((uint)token != 0);
            _methodDesc = methodDesc;
            _token = token;
        }

        protected override int ClassCode => 322554200;

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            ReadyToRunCodegenNodeFactory r2rFactory = (ReadyToRunCodegenNodeFactory)factory;
            ObjectDataBuilder dataBuilder = new ObjectDataBuilder();
            dataBuilder.AddSymbol(this);

            if (!relocsOnly)
            {
                ReadyToRunFixupKind fixupKind;
                if (_methodDesc.IsVirtual)
                {
                    fixupKind = ReadyToRunFixupKind.READYTORUN_FIXUP_VirtualEntry_RefToken;
                }
                else
                {
                    fixupKind = ReadyToRunFixupKind.READYTORUN_FIXUP_MethodEntry_RefToken;

                }
                dataBuilder.EmitByte((byte)fixupKind);
                SignatureBuilder.EmitTokenRid(ref dataBuilder, (int)_token);
            }

            return dataBuilder.ToObjectData();
        }

        public override void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix);
            sb.Append("MethodImportSignature: " + _methodDesc.ToString());
        }

        protected override int CompareToImpl(SortableDependencyNode other, CompilerComparer comparer)
        {
            return _methodDesc.ToString().CompareTo(((MethodImportSignature)other)._methodDesc.ToString());
        }
    }
}
