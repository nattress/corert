// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Tracing.Etlx;

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
        private Dictionary<long, string> _testModuleIdToName = new Dictionary<long, string>();
        private Dictionary<string, HashSet<string>> _methodsJitted = new Dictionary<string, HashSet<string>>();
        
        public ReadyToRunJittedMethodsEventPipe(ICollection<string> testModuleNames, ICollection<string> testFolderNames)
        {
            _testModuleNames = testModuleNames;
            _testFolderNames = testFolderNames;
        }

        public void ParseTraceFile(string traceFile)
        {
            Console.WriteLine($"Reading trace file {traceFile} for JIT events");

            Console.WriteLine("Test modules:");
            foreach (var x in _testModuleNames)
            {
                Console.WriteLine(x);
            }

            Console.WriteLine("Test Folders:");
            foreach (var x in _testFolderNames)
            {
                Console.WriteLine(x);
            }
            var logOptions = new TraceLogOptions() 
            {
                KeepAllEvents = true,
                ConversionLogName = @"d:\repro\r2r\conversion.txt",
                AlwaysResolveSymbols = true,
            };

            var etlxFile = TraceLog.CreateFromEventPipeDataFile(traceFile, null, logOptions);
            var trace = TraceLog.OpenOrConvert(etlxFile, logOptions);

            Console.WriteLine($"trace.Events.Log.EventCount: {trace.Events.Log.EventCount}");
            Console.WriteLine($"trace.Events.Count(): {trace.Events.Count()}");
            
            if (trace.EventsLost > 0)
            {
                Console.WriteLine($"Warning: {trace.EventsLost} events were lost during trace production.");
            }
            if (trace.Truncated)
            {
                Console.WriteLine($"Warning: Large trace log got truncated.");
            }

            foreach (var m in trace.Parsers)
            {
                Console.WriteLine(m.ToString());
            }
            
            foreach (var m in trace.ModuleFiles)
            {
                Console.WriteLine(m.ToString());
            }

            // Select all loaded assemblies that match _testModulesNames or are in one of _testFolderNames
            var moduleLoads = trace.Events
                .Where(t =>
                    string.Equals(t.ProviderName, "Microsoft-Windows-DotNETRuntime") &&
                    string.Equals(t.EventName, "Loader/ModuleLoad") &&
                    (ShouldMonitorModule(t.PayloadStringByName("ModuleILPath")) || ShouldMonitorModule(t.PayloadStringByName("ModuleNativePath"))))
                .Select(e => e.Clone())
                .ToList();

            Console.WriteLine($"Found {moduleLoads.Count} test modules");
            AssembliesWithEventsCount = moduleLoads.Count;

            // For each monitored module, select all method load events where the JIT was active
            foreach (var module in moduleLoads)
            {
                var jitEvents = trace.Events
                    .Where(t =>
                        string.Equals(t.ProviderName, "Microsoft-Windows-DotNETRuntime") &&
                        string.Equals(t.EventName, "Method/LoadVerbose") &&
                        ((bool)t.PayloadByName("IsJitted")) == true &&
                        (int)t.PayloadByName("ModuleID") == (int)module.PayloadByName("ModuleID"))
                    .Select(e => e.Clone())
                    .ToList();
                
                string moduleName = Path.GetFileNameWithoutExtension(module.PayloadStringByName("ModuleILFileName"));
                Console.WriteLine($"Assembly {moduleName} contains {jitEvents.Count} events");
            }

            foreach (var evt in trace.Events)
            {
                Console.WriteLine(evt.ToString());
            }
        }

        private bool ShouldMonitorModule(string fileName)
        {
            if (File.Exists(fileName) && _testFolderNames.Contains(Path.GetDirectoryName(fileName).ToAbsoluteDirectoryPath().ToLower()))
                return true;

            if (_testModuleNames.Contains(fileName.ToLower()))
                return true;

            return false;
        }

        public IReadOnlyDictionary<string, HashSet<string>> JittedMethods => _methodsJitted;

        /// <summary>
        /// Returns the number of test assemblies that were loaded by the runtime
        /// </summary>
        public int AssembliesWithEventsCount {private set; get;}
    }
}
