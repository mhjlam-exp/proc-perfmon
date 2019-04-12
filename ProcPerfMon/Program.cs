using Mono.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ProcPerfMon
{
    public class Program
    {
        private static StreamWriter writer;

        private static void Log(string message, bool writeToConsole = false)
        {
            writer.WriteLine(message);

            if (writeToConsole)
            {
                Console.WriteLine(message);
            }

            writer.Flush();
        }

        static void Main(string[] args)
        {
			bool showHelp = false;
            bool verbose = false;

            TimeSpan timeRemaining;
            DateTime startLogTime = new DateTime();
            TimeSpan logDuration = new TimeSpan(0, 0, 120);
            TimeSpan logInterval = new TimeSpan(0, 0, 0, 0, 100);

            string processName = "explorer";
            string logFileName = string.Format("ProcPerfMon-{0:yyyyMMdd_HHmmss}.csv", DateTime.Now);

            List<string> nonOptionalArgs = new List<string>();

			int j = 1;
			while (File.Exists(logFileName))
			{
				logFileName = Path.GetFileNameWithoutExtension(logFileName) + $"-{j++}";
			}

            OptionSet optionSet = new OptionSet()
            {
                { "d=|duration", "Duration of the session (in seconds).", (int d) => logDuration = new TimeSpan(0, 0, d) },
                { "i=|interval", "Interval between recording steps (in milliseconds)", (int i) => logInterval = new TimeSpan(0, 0, 0, 0, i) },
				{ "f=|filename", "Location and name of the log file.", f => logFileName = f },
                { "v|verbose", "Print logging output to screen.", v => verbose = v != null },
				{ "h|help",  "Show this message and exit.", h => showHelp = h != null }
			};

            try
			{
				FileInfo fileInfo = new FileInfo(logFileName);
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"Invalid log file {logFileName}: {e.Message}");
				return;
			}

			// Append .log extension to specified log file if necessary
			if (Path.GetExtension(logFileName) != ".csv")
			{
				logFileName += ".csv";
			}

			try
			{
				nonOptionalArgs = optionSet.Parse(args);
			}
			catch (OptionException e)
			{
                Console.Error.WriteLine($"Invalid arguments: {e.Message}");
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
            uint selection = 0;
            Process targetProcess;
            List<Process> processes = new List<Process>();

            foreach (Process process in Process.GetProcesses().Where(p => p.MainWindowTitle.Length > 0).OrderBy(p => p.ProcessName).ToList())
            {
                try
                {
                    ProcessModule processModule = process.MainModule;
                    processes.Add(process);
                }
                catch (Exception) {}
            }

			if (nonOptionalArgs.Count == 0)
			{
				Console.WriteLine("Select the target process to monitor:");

				for (int i = 0; i < processes.Count; ++i)
				{
                    try
                    {
                        Console.WriteLine("[{0,2}]  {1,-12}  {2,32}", i + 1, processes[i].ProcessName, processes[i].MainModule.FileName);
                    }
                    catch (Exception) {}
				}

                string selectionString = Console.ReadLine();
                if (!uint.TryParse(selectionString, out selection))
                {
                    Console.Error.WriteLine("Invalid input.");
                    return;
                }

                if (selection > processes.Count)
                {
                    Console.Error.WriteLine("Invalid input.");
                    return;
                }

                processName = processes[(int)selection-1].ProcessName;
            }
            else if (nonOptionalArgs.Count > 0)
			{
				processName = nonOptionalArgs[0];
			}

			// Make sure that process is currently running
			List<Process> matchingProcesses = processes.FindAll(x => x.ProcessName == processName);

			if (matchingProcesses.Any(p => p.MainWindowTitle.Length == 0))
			{
				Console.Error.WriteLine($"Access is denied to target process {processName}.");
				return;
			}

			if (matchingProcesses.Count == 0)
			{
                Console.Error.WriteLine($"Target process {processName} is not currently running.");
				return;
			}

            // Ask for more input if more than one process with target name is running
            selection = 0;
            if (matchingProcesses.Count > 1)
            {
                Console.WriteLine("More than one process with the specified name is currently running. Select the target process to monitor:");

                for (int i = 0; i < matchingProcesses.Count; ++i)
                {
                    Console.WriteLine("[{0,2}]  {1,12}  {2,32}", i + 1, matchingProcesses[i].ProcessName, matchingProcesses[i].MainModule.FileName);
                }

                ConsoleKeyInfo cki = Console.ReadKey(true);
                if (char.IsNumber(cki.KeyChar))
                {
                    selection = uint.Parse(cki.KeyChar.ToString()) - 1;
                    if (selection > matchingProcesses.Count)
                    {
                        Console.Error.WriteLine("Invalid input.");
                        return;
                    }
                }
                else
                {
                    Console.Error.WriteLine("Invalid input, aborting.");
                    return;
                }
            }

            targetProcess = matchingProcesses[(int)selection];
            processName = targetProcess.ProcessName;

            // Obtain load sensors for target process
            CpuSensor cpuSensor = new CpuSensor(targetProcess);
            RamSensor ramSensor = new RamSensor(targetProcess);
            GpuCoreSensor gpuSensor = new GpuCoreSensor();
            GpuVideoSensor videoSensor = new GpuVideoSensor();
            GpuVramSensor vramSensor = new GpuVramSensor();

            DateTime now = DateTime.Now;
            startLogTime = now;
            logFileName = string.Format("ProcPerfMon-{0}-{1:yyyyMMdd_HHmmss}.csv", processName, now);

            bool fileExists = File.Exists(logFileName);
            writer = new StreamWriter(new FileStream(logFileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite));

            if (!fileExists)
            {
                // Log header
                string header = "\"Timestamp\",";
                header += "\"" + cpuSensor.Name + " (%)\",";
                header += "\"" + ramSensor.Name + " (MB)\",";
                header += "\"" + ramSensor.Name + " (%)\",";
                header += "\"" + gpuSensor.Name + " (%)\",";
                header += "\"" + videoSensor.Name + " (%)\",";
                header += "\"" + vramSensor.Name + " (MB)\",";
                header += "\"" + vramSensor.Name + " (%)\"";

                Log(header, verbose);
            }

            while (now - startLogTime < logDuration)
            {
                // Exit if process ends during monitoring
                if (targetProcess.HasExited)
                {
                    writer.Flush();
                    break;
                }

                // End when time logging duration has passed
                timeRemaining = logDuration - (now - startLogTime);
                if (timeRemaining <= TimeSpan.Zero)
                {
                    break;
                }

                // Get new counter values for target process
                targetProcess.Refresh();

                // Log sensor data
                float cpu = cpuSensor.NextValue();
                float ram = ramSensor.NextValue();
                float gpu = gpuSensor.NextValue();
                float video = videoSensor.NextValue();
                float vram = vramSensor.NextValue();

                string value = "\"" + now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "\",";
                value += "\"" + cpu + "\",";
                value += "\"" + ram + "\",";
                value += "\"" + ram / ramSensor.TotalRam * 100f + "\",";
                value += "\"" + gpu + "\",";
                value += "\"" + video + "\",";
                value += "\"" + vram + "\",";
                value += "\"" + vram / vramSensor.TotalVram * 100f + "\"";

                Log(value, verbose);

                now = DateTime.Now;
                Thread.Sleep(logInterval.Milliseconds);
            }
        }
    }
}
