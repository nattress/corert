// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using Internal.Runtime;

namespace ILCompiler.PEWriter
{
    internal struct SectionEntry
    {
        public readonly ReadyToRunSectionType SectionType;
        public readonly int RelativeVirtualAddress;
        public readonly int Size;

        public SectionEntry(ReadyToRunSectionType sectionType, int relativeVirtualAddress, int size)
        {
            SectionType = sectionType;
            RelativeVirtualAddress = relativeVirtualAddress;
            Size = size;
        }
    }

    /// <summary>
    /// Reads the ready-to-run header and section directory from an existing image.
    /// This is used to strip existing ready-to-run artifacts from a binary being compiled
    /// so compilation is idempotent.
    /// </summary>
    internal sealed class ReadyToRunHeaders
    {
        public readonly IReadOnlyList<SectionEntry> SectionEntries;

        public ReadyToRunHeaders(PEReader peReader)
        {
            DirectoryEntry r2rHeaderDirectory = peReader.PEHeaders.CorHeader.ManagedNativeHeaderDirectory;
            BlobReader headerReader = peReader.GetEntireImage().GetReader(RvaToFilePosition(peReader, r2rHeaderDirectory.RelativeVirtualAddress), r2rHeaderDirectory.Size);

            // ReadyToRunHeader.Magic
            headerReader.ReadInt32();

            // Version
            headerReader.ReadInt16();
            headerReader.ReadInt16();

            // Flags
            headerReader.ReadInt32();

            var sectionEntries = new List<SectionEntry>();
            int sectionCount = headerReader.ReadInt32();

            for (int i = 0; i < sectionCount; i++)
            {
                int sectionId = headerReader.ReadInt32();
                int sectionRva = headerReader.ReadInt32();
                int sectionSize = headerReader.ReadInt32();

                sectionEntries.Add(new SectionEntry((ReadyToRunSectionType)sectionId, sectionRva, sectionSize));
            }

            // Order by section start RVA ascending to ease skipping sections of the input image
            sectionEntries.Sort((x, y) => x.RelativeVirtualAddress.CompareTo(y.RelativeVirtualAddress));
            SectionEntries = sectionEntries;
        }

        /// <summary>
        /// Returns True iff the COR header contains the ILLibrary flag indicating it is a ready-to-run image
        /// </summary>
        /// <param name="peReader"></param>
        /// <returns></returns>
        public static bool IsReadyToRunImage(PEReader peReader)
            => (peReader.PEHeaders.CorHeader.Flags & CorFlags.ILLibrary) != 0;

        public IEnumerable<SectionEntry> GetSectionsForRvaRange(int startRva, int endRva)
        {
            foreach (var x in SectionEntries)
            {
                if (x.RelativeVirtualAddress >= startRva && x.RelativeVirtualAddress < endRva)
                    yield return x;
            }
        }

        private int RvaToFilePosition(PEReader peReader, int rva)
        {
            int index = peReader.PEHeaders.GetContainingSectionIndex(rva);
            if (index == -1)
            {
                throw new BadImageFormatException($"Failed to convert invalid RVA to offset: {rva}");
            }
            SectionHeader containingSection = peReader.PEHeaders.SectionHeaders[index];
            return rva - containingSection.VirtualAddress + containingSection.PointerToRawData;
        }
    }
}
