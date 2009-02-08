﻿//
// Copyright (c) 2008-2009, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DiscUtils.Xva
{
    public class VirtualMachineBuilder : StreamBuilder
    {
        private Dictionary<string, SparseStream> _disks;

        public VirtualMachineBuilder()
        {
            _disks = new Dictionary<string, SparseStream>();
        }

        public void AddDisk(string uuid, SparseStream content)
        {
            _disks.Add(uuid, content);
        }

        public void AddDisk(string uuid, Stream content)
        {
            _disks.Add(uuid, SparseStream.FromStream(content, false));
        }

        public override SparseStream Build()
        {
            TarFileBuilder tarBuilder = new TarFileBuilder();

            int[] diskIds;

            string ovaFileContent = GenerateOvaXml(out diskIds);
            tarBuilder.AddFile("ova.xml", Encoding.ASCII.GetBytes(ovaFileContent));

            int diskIdx = 0;
            foreach (var diskRec in _disks)
            {
                SparseStream diskStream = diskRec.Value;
                List<StreamExtent> extents = new List<StreamExtent>(diskStream.Extents);

                int lastChunkAdded = -1;
                foreach (StreamExtent extent in extents)
                {
                    int firstChunk = (int)(extent.Start / Sizes.OneMiB);
                    int lastChunk = (int)((extent.Start + extent.Length - 1) / Sizes.OneMiB);

                    for (int i = firstChunk; i <= lastChunk; ++i)
                    {
                        if (i != lastChunkAdded)
                        {
                            HashAlgorithm hashAlg = new SHA1Managed();
                            Stream chunkStream = new SubStream(diskStream, i * Sizes.OneMiB, Sizes.OneMiB);
                            HashStream chunkHashStream = new HashStream(chunkStream, true, hashAlg);

                            tarBuilder.AddFile(string.Format("Ref:{0}/{1:X8}", diskIds[diskIdx], i), chunkHashStream);
                            tarBuilder.AddFile(string.Format("Ref:{0}/{1:X8}.checksum", diskIds[diskIdx], i), new ChecksumStream(hashAlg));

                            lastChunkAdded = i;
                        }
                    }
                }
                ++diskIdx;
            }

            return tarBuilder.Build();
        }

        internal override List<BuilderExtent> FixExtents(out long totalLength)
        {
            // Not required - deferred to TarFileBuilder
            throw new NotSupportedException();
        }

        private string GenerateOvaXml(out int[] diskIds)
        {
            int id = 0;

            Guid vmGuid = Guid.NewGuid();
            string vmName = "VM";
            int vmId = id++;

            // Establish Ids
            Guid[] vbdGuids = new Guid[_disks.Count];
            int[] vbdIds = new int[_disks.Count];
            Guid[] vdiGuids = new Guid[_disks.Count];
            string[] vdiNames = new string[_disks.Count];
            int[] vdiIds = new int[_disks.Count];
            for (int i = 0; i < _disks.Count; ++i)
            {
                vbdGuids[i] = Guid.NewGuid();
                vbdIds[i] = id++;
                vdiGuids[i] = Guid.NewGuid();
                vdiIds[i] = id++;
                vdiNames[i] = "VDI_" + i;
            }

            Guid srGuid = Guid.NewGuid();
            string srName = "SR";
            int srId = id++;


            string vbdRefs = "";
            for (int i = 0; i < _disks.Count; ++i)
            {
                vbdRefs += string.Format(Resources.XVA_ova_ref, "Ref:" + vbdIds[i]);
            }

            string vdiRefs = "";
            for (int i = 0; i < _disks.Count; ++i)
            {
                vdiRefs += string.Format(Resources.XVA_ova_ref, "Ref:" + vdiIds[i]);
            }


            StringBuilder objectsString = new StringBuilder();

            objectsString.Append(string.Format(Resources.XVA_ova_vm, "Ref:" + vmId, vmGuid, vmName, vbdRefs));

            for (int i = 0; i < _disks.Count; ++i)
            {
                objectsString.Append(string.Format(Resources.XVA_ova_vbd, "Ref:" + vbdIds[i], vbdGuids[i], "Ref:" + vmId, "Ref:" + vdiIds[i], i));
            }

            for (int i = 0; i < _disks.Count; ++i)
            {
                objectsString.Append(string.Format(Resources.XVA_ova_vdi, "Ref:" + vdiIds[i], vdiGuids[i], vdiNames[i], "Ref:" + srId, "Ref:" + vbdIds[i]));
            }

            objectsString.Append(string.Format(Resources.XVA_ova_sr, "Ref:" + srId, srGuid, srName, vdiRefs));

            diskIds = vdiIds;
            return string.Format(Resources.XVA_ova_base, objectsString.ToString());
        }
    }
}
