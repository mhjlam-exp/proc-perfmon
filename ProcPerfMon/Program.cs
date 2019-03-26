using Mono.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ProcPerfMon
{
    public class Program
    {
		static void ShowUsage(OptionSet optionSet)
        {
            Console.WriteLine("Usage: ProcPerfMon [OPTIONS]+ process");
            Console.WriteLine("Run performance monitor on specified process.");
            Console.WriteLine("By default this program logs to a file in parent directory for 1 minute.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            optionSet.WriteOptionDescriptions(Console.Out);
        }

        static void Main(string[] args)
        {
			// Obtain "Process" performance counter category (since this is slow it is done at startup)
			PerformanceCounterCategory processCategory = PerformanceCounterCategory.GetCategories().FirstOrDefault(x => x.CategoryName == "Process");

			int logDuration = 60;
			bool showHelp = false;
			string processName = "devenv";
			string logFile = DateTime.Now.ToString($"{processName}-yyyyMMdd_HHmmss");
			List<string> nonOptionalArgs = new List<string>();

			int i = 1;
			while (File.Exists(logFile))
			{
				logFile = Path.GetFileNameWithoutExtension(logFile) + $"-{i++}";
			}

			OptionSet optionSet = new OptionSet()
			{
				{ "p|process", "Name of the process to monitor.", p => processName = p },
				{ "d|duration", "Duration of the session (in seconds).", (int d) => logDuration = d },
				{ "l|logfile", "Location and name of the log file.", l => logFile = l },
				{ "h|help",  "Show this message and exit.", v => showHelp = v != null },
			};

			try
			{
				FileInfo fileInfo = new FileInfo(logFile);
			}
			catch (Exception e)
			{
				Console.WriteLine($"Invalid log file {logFile}: {e.Message}");
				return;
			}

			// Append .log extension to specified log file if necessary
			if (Path.GetExtension(logFile) != ".log")
			{
				logFile += ".log";
			}

			try
			{
				nonOptionalArgs = optionSet.Parse(args);
			}
			catch (OptionException e)
			{
				Console.WriteLine($"Invalid arguments: {e.Message}");
				return;
			}

			if (showHelp)
			{
				ShowUsage(optionSet);
				return;
			}

			// Allow user to select process from list if no process name was given
			int selection = 0;
			Process targetProcess;
			List<Process> processes = Process.GetProcesses().Where(p => p.MainWindowTitle.Length > 0).OrderBy(p => p.ProcessName).ToList();

			if (nonOptionalArgs.Count == 0)
			{
				Console.WriteLine("Select the target process to monitor:");

				for (i = 0; i < processes.Count; ++i)
				{
					Console.WriteLine("[{0,2}]  {1,-12}  {2,32}", i+1, processes[i].ProcessName, processes[i].MainModule.FileName);
				}

				ConsoleKeyInfo cki = Console.ReadKey(true);
				if (char.IsNumber(cki.KeyChar))
				{
					selection = int.Parse(cki.KeyChar.ToString()) - 1;
					if (selection > processes.Count)
					{
						Console.WriteLine("Invalid input.");
						return;
					}

					processName = processes[selection].ProcessName;
				}
				else
				{
					Console.WriteLine("Invalid input, aborting.");
					return;
				}
			}
			else if (nonOptionalArgs.Count > 0)
			{
				processName = nonOptionalArgs[0];
			}

			// Make sure that process is currently running
			List<Process> matchingProcesses = processes.FindAll(x => x.ProcessName == processName);

			if (matchingProcesses.Any(p => p.MainWindowTitle.Length == 0))
			{
				Console.WriteLine($"Access is denied to target process {processName}.");
				return;
			}

			if (matchingProcesses.Count == 0)
			{
				Console.WriteLine($"Target process {processName} is not currently running.");
				return;
			}

			// Ask for more input if more than one process with target name is running
			selection = 0;
			if (matchingProcesses.Count > 1)
			{
				Console.WriteLine("More than one process with the specified name is currently running. Select the target process to monitor:");

				for (i = 0; i < matchingProcesses.Count; ++i)
				{
					Console.WriteLine("[{0,2}]  {1,12}  {2,32}", i+1, matchingProcesses[i].ProcessName, matchingProcesses[i].MainModule.FileName);
				}

				ConsoleKeyInfo cki = Console.ReadKey(true);
				if (char.IsNumber(cki.KeyChar))
				{
					selection = int.Parse(cki.KeyChar.ToString()) - 1;
					if (selection > matchingProcesses.Count)
					{
						Console.WriteLine("Invalid input.");
						return;
					}
				}
				else
				{
					Console.WriteLine("Invalid input, aborting.");
					return;
				}
			}

			targetProcess = matchingProcesses[selection];

			// Obtain CPU and RAM usage performance counters for target process
            PerformanceCounter ramCounter = new PerformanceCounter("Process", "Working Set", targetProcess.ProcessName);
            PerformanceCounter cpuCounter = new PerformanceCounter("Process", "% Processor Time", targetProcess.ProcessName);

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

                System.Threading.Thread.Sleep(200);
            }
        }
    }
}
