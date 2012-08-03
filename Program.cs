using System;
using System.Text;
using CommandLine;
using NLog;

namespace winlogcheck
{
	class Program
	{
		// Create instance for Nlog
		static public Logger log = LogManager.GetLogger("msbplaunch");

		static void Main(string[] args)
		{
			log.Info("WinLogCheck start");

			readCommanLineOptions(args);

			log.Info("WinLogCheck successfully");
		}

		static void readCommanLineOptions(string[] args)
		{
			var options = new Options();
			ICommandLineParser parser = new CommandLineParser();
			if (parser.ParseArguments(args, options))
			{
				if (options.Mode == "exclude" || options.Mode == "include")
				{
					Console.WriteLine(options.Mode);
					Console.WriteLine(String.Join(" ", options.LogName));
					Console.WriteLine(options.Rule);
				}
				else
				{
					Console.WriteLine(options.GetUsage());
					log.Error(String.Format("WinLogCheck stop with error in command line arguments: '{0}'", String.Join(" ", args)));
					Environment.Exit(2);
				}
			}
			else
			{
				Console.WriteLine(options.GetUsage());
				log.Error(String.Format("WinLogCheck stop with error in command line arguments: '{0}'", String.Join(" ", args)));
				Environment.Exit(2);
			}
		}
	}

	class Options
	{
		[Option("m", null, Required = true, HelpText = "Report mode")]
		public string Mode { get; set; }

		[OptionArray("l", null, HelpText = "Event log name")]
		public string[] LogName { get; set; }

		[Option("r", null, HelpText = "Rule name")]
		public string Rule { get; set; }

		[HelpOption]
		public string GetUsage()
		{
			// this without using CommandLine.Text
			var usage = new StringBuilder();
			usage.AppendLine("WinLogCheck v.6.0");
			usage.AppendLine();
			usage.AppendLine("Options:");
			usage.AppendLine("\t-m <exclude|include>");
			usage.AppendLine("\t\tReport mode. Required");
			usage.AppendLine("\t-l <logname>");
			usage.AppendLine("\t\tEvent Log Name.");
			usage.AppendLine("\t-r <rulename>");
			usage.AppendLine("\t\tFilter rule name");
			usage.AppendLine();
			usage.AppendLine("Quick usage:");
			usage.AppendLine("\t- winlogcheck -m exclude");
			usage.AppendLine("\t\tGet report about 'unknown' events");
			usage.AppendLine("\t- winlogcheck -m include -l application -r webevents");
			usage.AppendLine("\t\tGet report satisfies the rule filter 'webevents'\n\t\tfrom application eventlog.");
			usage.AppendLine();

			usage.AppendLine("Read more in user manual...");
			return usage.ToString();
		}
	}
}
