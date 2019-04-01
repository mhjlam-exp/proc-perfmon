using Mono.Options;
using ProcPerfMon.Nvidia;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ProcPerfMon
{
    public class Program
    {
        static void ShowUsage(OptionSet optionSet)
        {

        }

        static void Main(string[] args)
        {
            // Obtain "Process" performance counter category (since this is slow it is done at startup)
            //List<PerformanceCounterCategory> categories = PerformanceCounterCategory.GetCategories().ToList();

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
                Console.WriteLine("Usage: ProcPerfMon [OPTIONS]+ process");
                Console.WriteLine("Run performance monitor on specified process.");
                Console.WriteLine("By default this program logs to a file in parent directory for 1 minute.");
                Console.WriteLine();
                Console.WriteLine("Options:");
                optionSet.WriteOptionDescriptions(Console.Out);
                return;
			}

			// Allow user to select process from list if no process name was given
            List<Process> processes = new List<Process>();
			List<Process> allProcesses = Process.GetProcesses().ToList();

            foreach (Process process in Process.GetProcesses().Where(p => p.MainWindowTitle.Length > 0).OrderBy(p => p.ProcessName).ToList())
            {
                try
                {
                    ProcessModule processModule = process.MainModule;
                    processes.Add(process);
                }
                catch (Exception) { }
            }

			int selection = 0;
			if (nonOptionalArgs.Count == 0)
			{
				Console.WriteLine("Select the target process to monitor:");

				for (i = 0; i < processes.Count; ++i)
				{
                    try
                    {
                        Console.WriteLine("[{0,2}]  {1,-12}  {2,32}", i + 1, processes[i].ProcessName, processes[i].MainModule.FileName);
                    }
                    catch (Exception) {}
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
			List<Process> matchingProcesses = allProcesses.FindAll(x => x.ProcessName == processName);

			if (matchingProcesses.Count == 0)
			{
				Console.WriteLine($"Target process {processName} is not currently running.");
				return;
			}

            // Obtain sensors for target process
            Sensor cpuSensor = new CpuSensor(matchingProcesses);
            Sensor ramSensor = new RamSensor(matchingProcesses);
            Sensor gpuSensor = new GpuCoreSensor();
            Sensor videoSensor = new GpuVideoSensor();
            Sensor vramSensor = new GpuVramSensor();

            while (!Console.KeyAvailable)
            {
				foreach (Process process in matchingProcesses)
				{
					process.Refresh();
				}

                Console.WriteLine("{0:-16}  {1:##0.000}", "CPU Load", cpuSensor.NextValue());
                Console.WriteLine("{0:-16}  {1:##0.000}", "RAM Load", ramSensor.NextValue());

                Console.WriteLine("{0:-16}  {1:##0.000}", gpuSensor.Name, gpuSensor.NextValue());
                Console.WriteLine("{0:-16}  {1:##0.000}", videoSensor.Name, videoSensor.NextValue());
                Console.WriteLine("{0:-16}  {1:##0.000}", vramSensor.Name, vramSensor.NextValue());

                Console.WriteLine();

                System.Threading.Thread.Sleep(200);
            }
        }
    }
}
