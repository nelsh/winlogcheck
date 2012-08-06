using System;
using System.Collections;
using System.IO;
using System.Text;
using CommandLine;
using IniParser;
using NLog;

namespace winlogcheck
{
	class Program
	{
		// Create instance for Nlog
		static public Logger log = LogManager.GetLogger("msbplaunch");
		// Instance for store command line arguments
		static public Options options = new Options();
		// struct for store program settings
		static public ProgramSettings currentSettings;
		// time line for all events over the past day
		static string whereTime = "";

		static void Main(string[] args)
		{
			Program.whereTime = DateTime.UtcNow.AddDays(-1).ToString("yyyyMMdd HH:mm:ss");
			log.Info("WinLogCheck start");

			// Read and check command line arguments
			readCommanLineOptions(args);
			// Read and check program settings
			currentSettings = new ProgramSettings(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
				System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".ini"));


			if (!String.IsNullOrWhiteSpace(options.LogName))
			{
				if (!String.IsNullOrWhiteSpace(options.Filter))
				{
					// Get specific report in specific eventlog
					log.Info(String.Format("Mode: '{0}', filter: '{1}' in eventlog '{2}'", options.Mode, options.Filter, options.LogName));
					getReportFilter(options.Mode, options.LogName, options.Filter);
				}
				else
				{
					// Get report in specific eventlog
					log.Info(String.Format("Mode '{0}',  ALL filters in eventlog '{1}'", options.Mode, options.LogName));
					getReportLog(options.Mode, options.LogName);
				}
			}
			else
			{
				// Get full report
				log.Info(String.Format("Mode '{0}', ALL filters in ALL eventlogs", options.Mode));
			}
			

			log.Info("WinLogCheck successfully");
		}

		static void readCommanLineOptions(string[] args)
		{
			ICommandLineParser parser = new CommandLineParser();
			if (parser.ParseArguments(args, options))
			{
				if (options.Mode == "exclude" || options.Mode == "include")
				{
					log.Debug(String.Format("Mode: {0}", options.Mode));
					if (options.LogNameArray == null || options.LogNameArray.Length < 1)
						options.LogName = "";
					else
						options.LogName = String.Join(" ", options.LogNameArray);
					log.Debug(String.Format("EventLog: {0}", options.LogName));
					log.Debug(String.Format("Filter: {0}", options.Filter));
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

		static void getReportFilter(string mode, string eventlog, string filter)
		{
			ArrayList filters = readFilter(mode, eventlog, filter);
			getEvents(mode, eventlog, filters);
		}

		static void getReportLog(string mode, string eventlog)
		{
			ArrayList filters = readFilter(mode, eventlog);
			getEvents(mode, eventlog, filters);
		}

		static void getReport(string mode, string eventlog)
		{
		}

		static ArrayList readFilter(string mode, string eventlog, string filter = "")
		{
			// array to store filters
			ArrayList filters = new ArrayList();
			// get filename for filters
			string filterFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, mode, eventlog + ".conf");
			Program.log.Debug(String.Format("Read filter file \"{0}\"", filterFile));
			// if usign filter and file not found - exit
			// else return empty filters
			if (!File.Exists(filterFile))
			{
				if (!String.IsNullOrWhiteSpace(filter))
				{
					Program.log.Error(String.Format("WinLogCheck stop: Cannot read {0}. Nothing do.", filterFile));
					Environment.Exit(2);
				}
				else
				{
					Program.log.Warn(String.Format("Cannot read {0}. Filters DISABLED.", filterFile));
					return filters;
				}

			}
			// read INI-file
			// if usign filter and file not read - exit
			// else return empty filters
			IniParser.FileIniDataParser iniParser = new FileIniDataParser();
			iniParser.KeyValueDelimiter = ':';
			IniData iniData = null;
			try
			{
				iniData = iniParser.LoadFile(filterFile);
			}
			catch (Exception ex)
			{
				if (!String.IsNullOrWhiteSpace(filter))
				{
					Program.log.Error(String.Format("WinLogCheck stop: Cannot read '{0}'. {1}", filterFile, ex));
					Environment.Exit(2);
				}
				else
				{
					Program.log.Warn(String.Format("Cannot read {0}. Filters DISABLED. {1}", filterFile, ex));
					return filters;
				}
			}
			// if usign filter and it not read - exit
			// else read all filters from file
			if (!String.IsNullOrWhiteSpace(filter))
			{
				try
				{
					filters.Add(iniData["General"][filter]);
					Program.log.Debug(String.Format("Use filter '{0}'", filter));
				}
				catch (Exception ex)
				{
					Program.log.Error(String.Format("WinLogCheck stop: Cannot read filter '{0}'. {1}", filter, ex));
					Environment.Exit(2);
				}
			}
			else
			{
				foreach (KeyData key in iniData.Sections["General"])
				{
					filters.Add(key.Value);
					Program.log.Debug(String.Format("Use filter '{0}' = {1}", key.KeyName, key.Value));
				}
			}
			return filters;
		}

		static void getEvents(string mode, string log, ArrayList filters)
		{
		}
	
	}

	class Options
	{
		public string LogName { get; set; }

		[Option("m", null, Required = true, HelpText = "Report mode")]
		public string Mode { get; set; }

		[Option("f","filter", HelpText = "Filter name")]
		public string Filter { get; set; }

		[OptionArray("l", null, HelpText = "Event log name")]
		public string[] LogNameArray { get; set; }

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
			usage.AppendLine("\t-f <filtername>");
			usage.AppendLine("\t\tFilter name");
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

	/// <summary>
	/// Struct for store settings
	/// </summary>
	public struct ProgramSettings
	{
		public string ReportPath;	// Path to store temporary current report
		// Mail settings
		public string SMTP_Server;
		public string Mail_From;
		public string Mail_To;
		public bool SendReport;

		public ProgramSettings(string iniFileName)
		{
			// is exists INI-file?
			Program.log.Debug(String.Format("Read INI File \"{0}\"", iniFileName));
			if (!File.Exists(iniFileName))
			{
				Program.log.Fatal("WinLogCheck stop: Cannot find INI File");
				Environment.Exit(2);
			}
			// read INI-file
			IniParser.FileIniDataParser iniParser = new FileIniDataParser();
			IniData iniData = null;
			try
			{
				iniData = iniParser.LoadFile(iniFileName);
			}
			catch (Exception ex)
			{
				Program.log.Fatal("WinLogCheck stop: Cannot read INI File. " + ex);
				Environment.Exit(2);
			}
			// check report path and create it if not exists
			ReportPath = "";
			try
			{
				ReportPath = iniData["General"]["ReportPath"];
				if (String.IsNullOrWhiteSpace(ReportPath))
					ReportPath = "reports";
				Program.log.Info(String.Format("Use directory \"{0}\" for temporary report.", ReportPath));
			}
			catch 
			{
				ReportPath = "reports";
				Program.log.Info(String.Format("Use default directory \"{0}\" for temporary report.", ReportPath));
			}
			if (!Directory.Exists(ReportPath))
			{
				Program.log.Info(String.Format("Not exist directory \"{0}\". Create it.", ReportPath));
				try
				{
					Directory.CreateDirectory(ReportPath);
				}
				catch (Exception ex)
				{
					Program.log.Fatal(String.Format("WinLogCheck stop: Cannot create directory {0}. Stack: {1}", ReportPath, ex));
					Environment.Exit(2);
				}
			}
			// check mail settings
			SMTP_Server = "";
			Mail_From = "";
			Mail_To = "";
			try
			{
				SMTP_Server = iniData["Mail"]["SMTP_Server"];
				Mail_From = iniData["Mail"]["Mail_From"];
				Mail_To = iniData["Mail"]["Mail_To"];
				if (String.IsNullOrWhiteSpace(SMTP_Server) || String.IsNullOrWhiteSpace(Mail_From) || String.IsNullOrWhiteSpace(Mail_To))
				{
					SendReport = false;
					Program.log.Warn("Check mail settings. Send summary report DISABLED");
				}
				else
				{
					SendReport = true;
					Program.log.Info("Send summary report ENABLED");
				}
			}
			catch
			{
				SendReport = false;
				Program.log.Warn("Check mail settings. Send summary report DISABLED");
			}
		}
	}

}
