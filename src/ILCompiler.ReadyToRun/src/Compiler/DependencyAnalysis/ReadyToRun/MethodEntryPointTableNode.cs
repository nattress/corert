// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata.Ecma335;

using Internal.NativeFormat;
using Internal.Runtime;
using Internal.Text;
using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    public class MethodEntryPointTableNode : HeaderTableNode
    {
        private struct EntryPoint
        {
            public static EntryPoint Null = new EntryPoint(-1, -1);
            
            public readonly int MethodIndex;
            public readonly int FixupIndex;

            public bool IsNull => (MethodIndex < 0);
            
            public EntryPoint(int methodIndex, int fixupIndex)
            {
                MethodIndex = methodIndex;
                FixupIndex = fixupIndex;
            }
        }

        /// <summary>
        /// This helper structure represents the "coordinates" of a single
        /// indirection cell in the import tables (index of the import
        /// section table and offset within the table).
        /// </summary>
        private struct FixupCell
        {
            public static readonly IComparer<FixupCell> Comparer = new CellComparer();

            public int TableIndex;
            public int ImportOffset;

            public FixupCell(int tableIndex, int importOffset)
            {
                TableIndex = tableIndex;
                ImportOffset = importOffset;
            }

            private class CellComparer : IComparer<FixupCell>
            {
                public int Compare(FixupCell a, FixupCell b)
                {
                    int result = a.TableIndex.CompareTo(b.TableIndex);
                    if (result == 0)
                    {
                        result = a.ImportOffset.CompareTo(b.ImportOffset);
                    }
                    return result;
                }
            }
        }

        List<EntryPoint> _ridToEntryPoint;

        List<byte[]> _uniqueFixups;
        Dictionary<byte[], int> _uniqueFixupIndex;
        
        public MethodEntryPointTableNode(TargetDetails target)
            : base(target)
        {
            _ridToEntryPoint = new List<EntryPoint>();

            _uniqueFixups = new List<byte[]>();
            _uniqueFixupIndex = new Dictionary<byte[], int>(ByteArrayComparer.Instance);
        }
        
        public override void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix);
            sb.Append("__ReadyToRunMethodEntryPointTable");
        }

        public void Add(MethodCodeNode methodNode, int methodIndex, NodeFactory factory)
        {
            uint rid;
            if (methodNode.Method is EcmaMethod ecmaMethod)
            {
                // Strip away the token type bits, keep just the low 24 bits RID
                rid = SignatureBuilder.RidFromToken(MetadataTokens.GetToken(ecmaMethod.Handle));
            }
            else if (methodNode.Method is MethodForInstantiatedType methodOnInstantiatedType)
            {
                if (methodOnInstantiatedType.GetTypicalMethodDefinition() is EcmaMethod ecmaTypicalMethod)
                {
                    rid = SignatureBuilder.RidFromToken(MetadataTokens.GetToken(ecmaTypicalMethod.Handle));
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
            else
            {
                throw new NotImplementedException();
            }

            Debug.Assert(rid != 0);
            rid--;

            byte[] fixups = GetFixupBlob(factory, methodNode);

            while (_ridToEntryPoint.Count <= rid)
            {
                _ridToEntryPoint.Add(EntryPoint.Null);
            }
    
            int fixupIndex = -1;
            if (fixups != null)
            {
                if (!_uniqueFixupIndex.TryGetValue(fixups, out fixupIndex))
                {
                    fixupIndex = _uniqueFixups.Count;
                    _uniqueFixupIndex.Add(fixups, fixupIndex);
                    _uniqueFixups.Add(fixups);
                }
            }


            _ridToEntryPoint[(int)rid] = new EntryPoint(methodIndex, fixupIndex);
        }

        private byte[] GetFixupBlob(NodeFactory factory, ObjectNode node)
        {
            Relocation[] relocations = node.GetData(factory, relocsOnly: true).Relocs;

            if (relocations == null)
            {
                return null;
            }

            List<FixupCell> fixupCells = null;

            foreach (Relocation reloc in relocations)
            {
                if (reloc.Target is Import fixupCell && fixupCell.IsDelayed)
                {
                    if (fixupCells == null)
                    {
                        fixupCells = new List<FixupCell>();
                    }
                    fixupCells.Add(new FixupCell(fixupCell.Table.Index, fixupCell.OffsetFromBeginningOfArray));
                }
            }

            if (fixupCells == null)
            {
                return null;
            }

            fixupCells.Sort(FixupCell.Comparer);

            NibbleWriter writer = new NibbleWriter();

            int curTableIndex = -1;
            int curOffset = 0;

            foreach (FixupCell cell in fixupCells)
            {
                Debug.Assert(cell.ImportOffset % factory.Target.PointerSize == 0);
                int offset = cell.ImportOffset / factory.Target.PointerSize;

                if (cell.TableIndex != curTableIndex)
                {
                    // Write delta relative to the previous table index
                    Debug.Assert(cell.TableIndex > curTableIndex);
                    if (curTableIndex != -1)
                    {
                        writer.WriteUInt(0); // table separator, so add except for the first entry
                        writer.WriteUInt((uint)(cell.TableIndex - curTableIndex)); // add table index delta
                    }
                    else
                    {
                        writer.WriteUInt((uint)cell.TableIndex);
                    }
                    curTableIndex = cell.TableIndex;

                    // This is the first fixup in the current table.
                    // We will write it out completely (without delta-encoding)
                    writer.WriteUInt((uint)offset);
                }
                else if (offset != curOffset) // ignore duplicate fixup cells
                {
                    // This is not the first entry in the current table.
                    // We will write out the delta relative to the previous fixup value
                    int delta = offset - curOffset;
                    Debug.Assert(delta > 0);
                    writer.WriteUInt((uint)delta);
                }

                // future entries for this table would be relative to this rva
                curOffset = offset;
            }

            writer.WriteUInt(0); // table separator
            writer.WriteUInt(0); // fixup list ends

            return writer.ToArray();
        }

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            NativeWriter arrayWriter = new NativeWriter();

            Section arraySection = arrayWriter.NewSection();
            VertexArray vertexArray = new VertexArray(arraySection);
            arraySection.Place(vertexArray);
            BlobVertex[] fixupBlobs = PlaceBlobs(arraySection, _uniqueFixups);

            for (int rid = 0; rid < _ridToEntryPoint.Count; rid++)
            {
                EntryPoint entryPoint = _ridToEntryPoint[rid];
                if (!entryPoint.IsNull)
                {
                    BlobVertex fixupBlobVertex = (entryPoint.FixupIndex >= 0 ? fixupBlobs[entryPoint.FixupIndex] : null);
                    EntryPointVertex entryPointVertex = new EntryPointVertex((uint)entryPoint.MethodIndex, fixupBlobVertex);
                    vertexArray.Set(rid, entryPointVertex);
                }
            }

            vertexArray.ExpandLayout();

            MemoryStream arrayContent = new MemoryStream();
            arrayWriter.Save(arrayContent);
            return new ObjectData(
                data: arrayContent.ToArray(),
                relocs: null,
                alignment: 8,
                definedSymbols: new ISymbolDefinitionNode[] { this });
        }

        private static BlobVertex[] PlaceBlobs(Section section, List<byte[]> blobs)
        {
            BlobVertex[] blobVertices = new BlobVertex[blobs.Count];
            for (int blobIndex = 0; blobIndex < blobs.Count; blobIndex++)
            {
                BlobVertex blobVertex = new BlobVertex(blobs[blobIndex]);
                section.Place(blobVertex);
                blobVertices[blobIndex] = blobVertex;
            }
            return blobVertices;
        }

        protected override int ClassCode => 787556329;
    }
}
