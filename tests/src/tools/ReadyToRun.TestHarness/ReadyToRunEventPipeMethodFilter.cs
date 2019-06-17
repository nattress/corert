// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing;

using Microsoft.Diagnostics.Tools.RuntimeClient;

namespace ReadyToRun.TestHarness
{
    //
    // Intercept module loads for assemblies we want to collect method Jit info for.
    // Each Method that gets Jitted from a ready-to-run assembly is interesting to look at.
    // For a fully r2r'd assembly, there should be no such methods, so that would be a test failure.
    //
    class ReadyToRunJittedMethodsEventPipe
    {
        private ICollection<string> _testModuleNames;
        private ICollection<string> _testFolderNames;
        private List<long> _testModuleIds = new List<long>();
        private Dictionary<long, string> _testModuleIdToName = new Dictionary<long, string>();
        private Dictionary<string, HashSet<string>> _methodsJitted = new Dictionary<string, HashSet<string>>();
        private int _pid = -1;
        private ulong _sessionId = 0;
        private static uint DefaultCircularBufferSizeInMB => 256;
        private Stream _collectionStream;
        public ReadyToRunJittedMethodsEventPipe(ICollection<string> testModuleNames, ICollection<string> testFolderNames)
        {
            _testModuleNames = testModuleNames;
            _testFolderNames = testFolderNames;
        }

        public async Task StartCollection(int pid)
        {
            string output = "d:\\repro\\r2r\\temppipefile";
            _pid = pid;
            //_pid = 43552;
            try
            {
                Debug.Assert(output != null);
                
                Provider[] providerCollection = new[] { new Provider("Microsoft-Windows-DotNETRuntime", (ulong)ClrTraceEventParser.Keywords.Default, EventLevel.Informational) };

                var process = Process.GetProcessById(_pid);
                var configuration = new SessionConfiguration(
                    circularBufferSizeMB: DefaultCircularBufferSizeInMB,
                    outputPath: output, // Not used on the streaming scenario.
                    providers: providerCollection);

                ulong sessionId = 0;
                _collectionStream = EventPipeClient.CollectTracing(_pid, configuration, out sessionId);
                //EventPipeEventSource source = new EventPipeEventSource()
                if (sessionId == 0)
                {
                    throw new Exception("Unable to create session.");
                }
                /*
                using (Stream stream = EventPipeClient.CollectTracing(_pid, configuration, out sessionId))
                {
                    if (sessionId == 0)
                    {
                        throw new Exception("Unable to create session.");
                    }
                    if (File.Exists(output))
                    {
                        throw new Exception($"Unable to create file {output}");
                    }

                    var collectingTask = new Task(() => {
                        try
                        {
                            using (var fs = new FileStream(output, FileMode.Create, FileAccess.Write))
                            {
                                Console.Out.WriteLine($"Process     : {process.MainModule.FileName}");
                                Console.Out.WriteLine($"Output File : {fs.Name}");
                                Console.Out.WriteLine($"\tSession Id: 0x{sessionId:X16}");
                                
                                while (true)
                                {
                                    var buffer = new byte[16 * 1024];
                                    int nBytesRead = stream.Read(buffer, 0, buffer.Length);
                                    if (nBytesRead <= 0)
                                        break;
                                    fs.Write(buffer, 0, nBytesRead);

                                    Debug.WriteLine($"PACKET: {Convert.ToBase64String(buffer, 0, nBytesRead)} (bytes {nBytesRead})");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                        }
                    });

                    collectingTask.Start();

                    await collectingTask;
                }
                 */
                Console.Out.WriteLine();
                Console.Out.WriteLine("Trace completed.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
            }
        }

        public void StopCollection()
        {
            EventPipeClient.StopTracing(_pid, _sessionId);
        }

        
        /// <summary>
        /// Set the process to monitor events for given its Id. This should be set immediately after
        /// calling Process.Start to ensure no module load events are missed for the runtime instance.
        /// </summary>
        public void SetProcessId(int pid)
        {
            _pid = pid;
        }

        public IReadOnlyDictionary<string, HashSet<string>> JittedMethods => _methodsJitted;

        /// <summary>
        /// Returns the number of test assemblies that were loaded by the runtime
        /// </summary>
        public int AssembliesWithEventsCount => _testModuleIds.Count;
    }
}
