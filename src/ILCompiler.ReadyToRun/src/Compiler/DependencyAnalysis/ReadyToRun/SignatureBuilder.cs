// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Internal.Runtime;
using Internal.TypeSystem;
using Internal.JitInterface;
using Debug = System.Diagnostics.Debug;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    public static class SignatureBuilder
    {
        public static void EmitData(ref ObjectDataBuilder dataBuilder, uint data)
        {
            if (data <= 0x7F)
            {
                dataBuilder.EmitByte((byte)data);
                return;
            }

            if (data <= 0x3FFF)
            {
                dataBuilder.EmitByte((byte)((data >> 8) | 0x80));
                dataBuilder.EmitByte((byte)(data & 0xFF));
                return;
            }

            if (data <= 0x1FFFFFFF)
            {
                dataBuilder.EmitByte((byte)((data >> 24) | 0xC0));
                dataBuilder.EmitByte((byte)((data >> 16) & 0xff));
                dataBuilder.EmitByte((byte)((data >> 8) & 0xff));
                dataBuilder.EmitByte((byte)(data & 0xff));
                return;
            }

            throw new NotImplementedException();
        }

        public static uint RidFromToken(mdToken token)
        {
            return unchecked((uint)token) & 0x00FFFFFFu;
        }

        public static uint TypeFromToken(int token)
        {
            return unchecked((uint)token) & 0xFF000000u;
        }

        public static uint TypeFromToken(mdToken token)
        {
            return TypeFromToken((int)token);
        }

        public static void EmitTokenRid(ref ObjectDataBuilder dataBuilder, mdToken token)
        {
            EmitData(ref dataBuilder, (uint)RidFromToken(token));
        }

        // compress a token
        // The least significant bit of the first compress byte will indicate the token type.
        //
        public static void EmitToken(ref ObjectDataBuilder dataBuilder, mdToken token)
        {
            uint rid = RidFromToken(token);
            CorTokenType type = (CorTokenType)TypeFromToken(token);

            if (rid > 0x3FFFFFF)
            {
                // token is too big to be compressed
                throw new NotImplementedException();
            }

            rid = (rid << 2);

            // TypeDef is encoded with low bits 00
            // TypeRef is encoded with low bits 01
            // TypeSpec is encoded with low bits 10
            // BaseType is encoded with low bit 11
            switch (type)
            {
                case CorTokenType.mdtTypeDef:
                    break;

                case CorTokenType.mdtTypeRef:
                    // make the last two bits 01
                    rid |= 0x1;
                    break;

                case CorTokenType.mdtTypeSpec:
                    // make last two bits 0
                    rid |= 0x2;
                    break;

                case CorTokenType.mdtBaseType:
                    rid |= 0x3;
                    break;

                default:
                    throw new NotImplementedException();
            }

            EmitData(ref dataBuilder, rid);
        }

        private static class SignMask
        {
            public const uint ONEBYTE = 0xffffffc0; // Mask the same size as the missing bits.
            public const uint TWOBYTE = 0xffffe000; // Mask the same size as the missing bits.
            public const uint FOURBYTE = 0xf0000000; // Mask the same size as the missing bits.
        }

        /// <summary>
        /// Compress a signed integer. The least significant bit of the first compressed byte will be the sign bit.
        /// </summary>
        public static void EmitSignedInt(ref ObjectDataBuilder dataBuilder, int data)
        {
            uint isSigned = (data < 0 ? 1u : 0u);
            uint udata = unchecked((uint)data);

            // Note that we cannot use CompressData to pack the data value, because of negative values 
            // like: 0xffffe000 (-8192) which has to be encoded as 1 in 2 bytes, i.e. 0x81 0x00
            // However CompressData would store value 1 as 1 byte: 0x01
            if ((udata & SignMask.ONEBYTE) == 0 || (udata & SignMask.ONEBYTE) == SignMask.ONEBYTE)
            {
                udata = ((udata & ~SignMask.ONEBYTE) << 1 | isSigned);
                Debug.Assert(udata <= 0x7f);
                dataBuilder.EmitByte((byte)udata);
                return;
            }

            if ((udata & SignMask.TWOBYTE) == 0 || (udata & SignMask.TWOBYTE) == SignMask.TWOBYTE)
            {
                udata = ((udata & ~SignMask.TWOBYTE) << 1 | isSigned);
                Debug.Assert(udata <= 0x3fff);
                dataBuilder.EmitByte((byte)((udata >> 8) | 0x80));
                dataBuilder.EmitByte((byte)(udata & 0xff));
                return;
            }

            if ((udata & SignMask.FOURBYTE) == 0 || (udata & SignMask.FOURBYTE) == SignMask.FOURBYTE)
            {
                udata = ((udata & ~SignMask.FOURBYTE) << 1 | isSigned);
                Debug.Assert(udata <= 0x1FFFFFFF);
                dataBuilder.EmitByte((byte)((udata >> 24) | 0xC0));
                dataBuilder.EmitByte((byte)((udata >> 16) & 0xff));
                dataBuilder.EmitByte((byte)((udata >> 8) & 0xff));
                dataBuilder.EmitByte((byte)(udata & 0xff));
                return;
            }

            // Out of compressable range
            throw new NotImplementedException();
        }


        /// <summary>
        /// Compress a CorElementType into a single byte.
        /// </summary>
        /// <param name="elementType">COR element type to compress</param>
        internal static void EmitElementType(ref ObjectDataBuilder dataBuilder, CorElementType elementType)
        {
            dataBuilder.EmitByte((byte)elementType);
        }

        public static void EmitType(ref ObjectDataBuilder dataBuilder, TypeDesc typeDesc, mdToken typeToken)
        {
            CorElementType elementType;
            if (typeDesc.IsPrimitive)
            {
                switch (typeDesc.Category)
                {
                    case TypeFlags.Void:
                        elementType = CorElementType.ELEMENT_TYPE_VOID;
                        break;

                    case TypeFlags.Boolean:
                        elementType = CorElementType.ELEMENT_TYPE_BOOLEAN;
                        break;

                    case TypeFlags.Char:
                        elementType = CorElementType.ELEMENT_TYPE_CHAR;
                        break;

                    case TypeFlags.SByte:
                        elementType = CorElementType.ELEMENT_TYPE_I1;
                        break;

                    case TypeFlags.Byte:
                        elementType = CorElementType.ELEMENT_TYPE_U1;
                        break;

                    case TypeFlags.Int16:
                        elementType = CorElementType.ELEMENT_TYPE_I2;
                        break;

                    case TypeFlags.UInt16:
                        elementType = CorElementType.ELEMENT_TYPE_U2;
                        break;

                    case TypeFlags.Int32:
                        elementType = CorElementType.ELEMENT_TYPE_I4;
                        break;

                    case TypeFlags.UInt32:
                        elementType = CorElementType.ELEMENT_TYPE_U4;
                        break;

                    case TypeFlags.Int64:
                        elementType = CorElementType.ELEMENT_TYPE_I8;
                        break;

                    case TypeFlags.UInt64:
                        elementType = CorElementType.ELEMENT_TYPE_U8;
                        break;

                    case TypeFlags.IntPtr:
                        elementType = CorElementType.ELEMENT_TYPE_I;
                        break;

                    case TypeFlags.UIntPtr:
                        elementType = CorElementType.ELEMENT_TYPE_U;
                        break;

                    case TypeFlags.Single:
                        elementType = CorElementType.ELEMENT_TYPE_R4;
                        break;

                    case TypeFlags.Double:
                        elementType = CorElementType.ELEMENT_TYPE_R4;
                        break;

                    default:
                        throw new NotImplementedException();
                }

                dataBuilder.EmitByte((byte)elementType);
                return;
            }

            /*
            if (typeDesc.IsString)
            {
                dataBuilder.EmitByte((byte)CorElementType.ELEMENT_TYPE_STRING);
                return;
            }
            */

            if (typeDesc is ArrayType arrayType)
            {
                dataBuilder.EmitByte((byte)CorElementType.ELEMENT_TYPE_SZARRAY);
                EmitType(ref dataBuilder, arrayType.ElementType, typeToken);
                return;
            }

            if (typeDesc.IsValueType)
            {
                elementType = CorElementType.ELEMENT_TYPE_VALUETYPE;
            }
            else
            {
                elementType = CorElementType.ELEMENT_TYPE_CLASS;
            }

            dataBuilder.EmitByte((byte)elementType);
            SignatureBuilder.EmitToken(ref dataBuilder, typeToken);
        }
    }
}
