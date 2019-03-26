using Mono.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ProcPerfMon
{
    public class Program
    {
        public const string ProcessName = "devenv";

        static void ShowUsage(OptionSet optionSet)
        {
            Console.WriteLine("Usage: ProcPerfMon [OPTIONS]+ process");
            Console.WriteLine("Run performance monitor on specified process.");
            Console.WriteLine("By default this program logs to a file in parent directory for 10 minutes.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            optionSet.WriteOptionDescriptions(Console.Out);
        }

        static void Main(string[] args)
        {
            //bool showHelp = false;

            //OptionSet optionSet = new OptionSet()
            //{
            //    { "n|name=", "the name of someone to greet.", v => names.Add(v) },
            //    { "r|repeat=", "the number of times to repeat the greeting.", (int v) => repeat = v },
            //    { "h|help",  "show this message and exit", v => showHelp = v != null },
            //};

            //List<string> extra;

            //try
            //{
            //    extra = optionSet.Parse(args);
            //}
            //catch (OptionException e)
            //{
            //    Console.WriteLine(e.Message);
            //    return;
            //}

            //string processName;
            //if (extra.Count > 0)
            //{
            //    message = string.Join(" ", extra.ToArray());
            //    Debug("Using new message: {0}", message);
            //}
            //else
            //{
            //    message = "Hello {0}!";
            //    Debug("Using default message: {0}", message);
            //}

            PerformanceCounterCategory processorCategory = PerformanceCounterCategory.GetCategories().FirstOrDefault(c => c.CategoryName == "Processor");
            PerformanceCounter[] processorCounters = processorCategory.GetCounters("_Total");

            Process[] processes = Process.GetProcessesByName(ProcessName);

            if (processes.Length < 1)
            {
                Console.WriteLine($"Unable to find any processes called {ProcessName}");
                return;
            }

            PerformanceCounterCategory processCategory = PerformanceCounterCategory.GetCategories().FirstOrDefault(x => x.CategoryName == "Process");

            PerformanceCounter ramCounter = new PerformanceCounter("Process", "Working Set", processes[0].ProcessName);
            PerformanceCounter cpuCounter = new PerformanceCounter("Process", "% Processor Time", processes[0].ProcessName);

            DisplayCounter(new PerformanceCounter[]{ ramCounter, cpuCounter });
        }

        private static void DisplayCounter(PerformanceCounter[] counters)
        {
            while (!Console.KeyAvailable)
            {
                foreach (PerformanceCounter counter in counters)
                {
                    Console.WriteLine($"{counter.CategoryName}\t{counter.CounterName} = {counter.NextValue()}");
                }

                System.Threading.Thread.Sleep(1000);
            }
        }
    }
}
