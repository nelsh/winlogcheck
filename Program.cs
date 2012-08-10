using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using CommandLine;
using IniParser;
using NLog;

namespace winlogcheck
{
	class Program
	{
		// Create instance for Nlog
		static public Logger log = LogManager.GetLogger("winlogcheck");
		// Instance for store command line arguments
		static public Options options = new Options();
		// struct for store program settings
		static public ProgramSettings currentSettings;
		// for store reading filter files errors
		static string readFilterError = "";
		// for store summary
		static int totalLogs = 0;
		static int totalEvents = 0;
		static int totalErrors = 0;
		static int totalWarnings = 0;
		static int totalOther = 0;

		static void Main(string[] args)
		{
			log.Info("WinLogCheck start");

			// Read and check command line arguments
			readCommanLineOptions(args);
			// Read and check program settings
			currentSettings = new ProgramSettings(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
				System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".ini"));


			if (!String.IsNullOrWhiteSpace(options.LogName))
			{
				///
				/// If  specify filter
				/// - Get specific report in specific eventlog
				///
				if (!String.IsNullOrWhiteSpace(options.Filter))
				{
					log.Info(String.Format("Mode: '{0}', Filter: '{1}' in Eventlog '{2}'", options.Mode, options.Filter, options.LogName));
					ArrayList filters = readFilter(options.Mode, options.LogName, options.Filter);
					// if filter not found - exit
					if (filters.Count < 1)
					{
						Program.log.Error("WinLogCheck stop with error:" + Program.readFilterError);
						Environment.Exit(2);
					}
					string eventsReport = getEventsReport(options.Mode, options.LogName, filters);
					writeEventsReport(eventsReport, options.Mode, options.LogName, options.Filter);
					if (currentSettings.SendReport) sendSummaryReport(options.Mode, options.LogName, options.Filter);
				}
				///
				/// If specify eventlog
				/// - Get report in specific eventlog
				///
				else
				{
					ArrayList filters = readFilter(options.Mode, options.LogName);
					log.Info(String.Format("Mode '{0}',  {1} Filters in Eventlog '{2}'", options.Mode, filters.Count, options.LogName));
					// If mode include and filter not found - exit
					if (filters.Count < 1 && options.Mode == "include")
					{
						Program.log.Error("WinLogCheck stop with error: " + Program.readFilterError);
						Environment.Exit(2);
					}
					// add default filter for security eventlog
					if (filters.Count < 1 && options.Mode == "exclude" && options.LogName.ToLower() == "security")
						filters.Add("EventType = 4");
					string eventsReport = getEventsReport(options.Mode, options.LogName, filters);
					writeEventsReport(eventsReport, options.Mode, options.LogName);
					if (currentSettings.SendReport) sendSummaryReport(options.Mode, options.LogName);
				}
			}
			///
			/// Get full report
			///
			else
			{
				StringBuilder eventsReport = new StringBuilder();
				log.Info(String.Format("Mode '{0}', ALL Filters in ALL Eventlogs", options.Mode));
				EventLog[] eventLogs = EventLog.GetEventLogs();
				foreach (EventLog e in eventLogs)
				{
					ArrayList filters = readFilter(options.Mode, e.Log);
					log.Info(String.Format(" - {0} Filters in Eventlog '{1}'", filters.Count, e.Log));
					// If mode include and filter not found - skip 
					if (filters.Count < 1 && options.Mode == "include")
					{
						string msg = String.Format("Eventlog {0} skip with warning: {1}", e.Log, Program.readFilterError);
						Program.log.Warn(String.Format(" - - {0}", msg));
						eventsReport.AppendLine(String.Format("<p><i>{0}</i></p>", msg));
						continue;
					}
					// add default filter for security eventlog
					if (filters.Count < 1 && options.Mode == "exclude" && e.Log.ToLower() == "security")
						filters.Add("EventType = 4");
					eventsReport.AppendLine(getEventsReport(options.Mode, e.Log, filters));
				}
				writeEventsReport(eventsReport.ToString(), options.Mode);
				// Send summary report
				if (currentSettings.SendReport) sendSummaryReport(options.Mode);
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

		static ArrayList readFilter(string mode, string eventlog, string filter = "")
		{
			// array to store filters
			ArrayList filters = new ArrayList();
			// get filename for filters
			string filterFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, mode, eventlog + ".conf");
			Program.log.Debug(String.Format("Read filter file \"{0}\"", filterFile));
			if (!File.Exists(filterFile))
			{
				Program.readFilterError = (String.Format("File not found '{0}'.", filterFile));
				return filters;
			}
			// read INI-file
			IniParser.FileIniDataParser iniParser = new FileIniDataParser();
			iniParser.KeyValueDelimiter = ':';
			iniParser.CommentDelimiter = '#';
			IniData iniData = null;
			try
			{
				iniData = iniParser.LoadFile(filterFile);
			}
			catch (Exception ex)
			{
				Program.readFilterError = String.Format("Cannot read file '{0}'. {1}", filterFile, ex);
				return filters;
			}
			if (!String.IsNullOrWhiteSpace(filter))
			{
				try
				{
					if (String.IsNullOrWhiteSpace(iniData["General"][filter]))
					{
						Program.readFilterError = String.Format("Filter '{0}' not found in '{1}'.", filter, filterFile);
						return filters;
					}
					filters.Add("(" + iniData["General"][filter] + ")");
					Program.log.Debug(String.Format("Use filter '{0}' : {1}", filter, iniData["General"][filter]));
				}
				catch (Exception ex)
				{
					Program.readFilterError = String.Format("Cannot read filter '{0}' in '{1}'. {2}", filter, filterFile, ex);
					return filters;
				}
			}
			else
			{
				foreach (KeyData key in iniData.Sections["General"])
				{
					filters.Add("(" + key.Value + ")");
					Program.log.Debug(String.Format("Use filter '{0}' = {1}", key.KeyName, key.Value));
				}
				if (filters.Count < 1)
					Program.readFilterError = "Filters empty.";
			}
			return filters;
		}

		static string getEventsReport(string mode, string eventlog, ArrayList filters)
		{
			// for store report
			StringBuilder reportString = new StringBuilder();

			// get total events in log
			string countString = String.Format("Select * From Win32_NTLogEvent Where LogFile = '{0}' and TimeGenerated >= '{1}'", eventlog, currentSettings.WhereTime);
			int events = 0;
			try
			{
				ManagementObjectSearcher evtCounter = new ManagementObjectSearcher();
				evtCounter.Scope.Options.EnablePrivileges = true;
				evtCounter.Query = new ObjectQuery(countString);
				events = evtCounter.Get().Count;
				evtCounter.Dispose();
			}
			catch
			{
				log.Error(String.Format("Unable get events from '{0}' eventlog", eventlog));
				reportString.AppendFormat("<table><caption class=error>Unable get events in <b>{0}</b></caption>", eventlog, filters.Count);
				reportString.AppendLine("<tr><td colspan=6>EMPTY</td></tr></table>");
				return reportString.ToString();
			}
			totalEvents += events;
			totalLogs++;
			log.Info(String.Format("Found {0} events in '{1}'", events, eventlog));
			if (events == 0)
			{
				reportString.AppendFormat("<table><caption>Eventlog name: <b>{0}</b>, use {1} filters</caption>", eventlog, filters.Count);
				reportString.AppendLine("<tr><td colspan=6>EMPTY</td></tr></table>");
				return reportString.ToString();
			}

			// default query string
			string queryString = String.Format("Select * From Win32_NTLogEvent Where LogFile = '{0}' and TimeGenerated >= '{1}'", eventlog, currentSettings.WhereTime);
			if (filters.Count > 0)
			{
				queryString = queryString + " AND ";
				if (mode == "exclude")
					queryString = queryString + " NOT ";
				queryString = queryString + "(" + String.Join(" OR ", filters.ToArray()) + ")";
			}
			Program.log.Debug(String.Format("Query: {0}",queryString));

			// get events
			ManagementObjectSearcher evtSearcher = new ManagementObjectSearcher();
			evtSearcher.Scope.Options.EnablePrivileges = true;
			evtSearcher.Query = new ObjectQuery(queryString);


			// format report
			reportString.AppendLine("<table>");
			reportString.AppendFormat("<caption>Eventlog name: <b>{0}</b>, use {1} filters. Check {2} events.</caption>", eventlog, filters.Count, events);
			int numberOfEvents = 0;
			foreach (ManagementObject logEvent in evtSearcher.Get())
			{
				if (numberOfEvents == 0)
				{
					reportString.AppendLine("<tr><th align=center>(!)</th><th>Time</th><th>Source</th><th>Category</th><th>EventID</th><th>User</th></tr>");
				}
				log.Trace("Event: {0} | Type: {1} | Time: {2}", ++numberOfEvents, logEvent["EventType"].ToString(), logEvent["TimeGenerated"].ToString());
				reportString.Append("<tr>");
				//reportString.AppendFormat("<td>{0}</td>", logEvent["EventType"]);
				switch (logEvent["EventType"].ToString())
				{
					case "1": totalErrors++; reportString.AppendFormat("<td class=error>{0}</td>", "Error"); break;
					case "2": totalWarnings++; reportString.AppendFormat("<td class=warning>{0}</td>", "Warning"); break;
					case "3": totalOther++; reportString.AppendFormat("<td class=info>{0}</td>", "Info"); break;
					case "4": totalOther++; reportString.AppendFormat("<td class=success>{0}</td>", "Success"); break;
					case "5": totalErrors++; reportString.AppendFormat("<td class=failure>{0}</td>", "Failure"); break;
					default: totalOther++; reportString.AppendFormat("<td>({0})</td>", logEvent["EventType"]); break;
				}
				reportString.AppendFormat("<td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td></tr>",
					DateTime.ParseExact(logEvent["TimeGenerated"].ToString().Substring(0, 14), "yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture).ToLocalTime().ToLongTimeString(),
					logEvent["SourceName"], logEvent["CategoryString"], logEvent["EventCode"], logEvent["User"]);
				if (logEvent["Message"] != null)
					reportString.AppendFormat("<tr><td></td><td colspan=5>{0}</td></tr>", logEvent["Message"].ToString().Replace("\r\n", "<br>"));
				else
					reportString.AppendFormat("<tr><td></td><td colspan=5>{0}</td></tr>", "none");
				reportString.AppendLine();
			}
			evtSearcher.Dispose();
			if (numberOfEvents == 0) { reportString.Append("<tr><td colspan=6>NONE EVENTS</td></tr>"); }
			reportString.AppendLine("</table>");
			return reportString.ToString();
		}

		static void writeEventsReport(string report, string mode, string eventlog = "", string filter = "")
		{
			string reportFile = getReportFileName(mode, eventlog, filter);
			Program.log.Debug(String.Format("Write temporary report to '{0}'", reportFile));
			StringBuilder titleString = new StringBuilder();
			//titleString.AppendFormat("WinLogCheck Report - {0} - ", System.Net.Dns.GetHostName().ToUpper());
			if (mode == "include")
				titleString.Append("Mode: INCLUDE, ");
			if (!String.IsNullOrWhiteSpace(eventlog))
				titleString.AppendFormat("Eventlog: {0}, ", eventlog.ToUpper());
			if (!String.IsNullOrWhiteSpace(filter))
				titleString.AppendFormat("Filter: {0}, ", filter.ToUpper());
			titleString.AppendFormat("Created: {0}", DateTime.Now);
			StringBuilder reportString = new StringBuilder();
			reportString.AppendLine("<!DOCTYPE html>");
			reportString.AppendLine("");
			reportString.AppendLine("<html><head><meta http-equiv=\"content-type\" content=\"text/html; charset=utf-8\" />");
			reportString.AppendFormat("<style>{0}</style>", currentSettings.ReportCSS);
			reportString.AppendFormat("<title>WinLogCheck Report - {0} - Found {1} events {2}</title></head><body>", 
				System.Net.Dns.GetHostName().ToUpper(), totalEvents, titleString);
			reportString.AppendFormat("<h1>WinLogCheck Report - <span class=servername>{0}</span></h1><div class=subhead>{1}</div>", 
				System.Net.Dns.GetHostName().ToUpper(), titleString);
			reportString.AppendFormat("<div class=subhead>Checked {0} events in {1} logs within the last {2} hours (errors={3}, warnings={4}, other={5})</div><br>",
				totalEvents, totalLogs, currentSettings.TimePeriod, totalErrors, totalWarnings, totalOther);
			reportString.AppendLine(report);
			reportString.AppendLine("</body></html>");
			try
			{
				System.IO.File.WriteAllText(reportFile, reportString.ToString());
			}
			catch
			{
				Program.log.Fatal(String.Format("WinLogCheck stop with error: Write temporary report to '{0}' failed", reportFile));
				Environment.Exit(2);
			}
		}

		static void sendSummaryReport(string mode, string eventlog = "", string filter = "")
		{
			string reportFile = getReportFileName(mode, eventlog, filter);
			string reportString = System.IO.File.ReadAllText(reportFile);

			//  Send report ////
			try
			{
				MailMessage mail = new MailMessage();
				SmtpClient SmtpServer = new SmtpClient(Program.currentSettings.SMTP_Server);
				mail.From = new MailAddress(Program.currentSettings.Mail_From);
				mail.To.Add(Program.currentSettings.Mail_To);
				mail.Subject = string.Format("Winlogcheck {0}: total {1} events in {2} logs within the last {3} hours (errors={4}, warnings={5}, other={6})",
					System.Net.Dns.GetHostName().ToUpper(), totalEvents, totalLogs, currentSettings.TimePeriod, totalErrors, totalWarnings, totalOther);
				mail.IsBodyHtml = true;
				mail.Body = reportString;
				SmtpServer.Send(mail);
				log.Info("Successfully send summary report");
			}
			catch (Exception ex)
			{
				log.Warn(ex.ToString());
			}
		}

		private static string getReportFileName(string mode, string eventlog = "", string filter = "")
		{
			string reportFile = mode;
			if (!String.IsNullOrWhiteSpace(eventlog))
				reportFile = reportFile + "." + eventlog;
			if (!String.IsNullOrWhiteSpace(filter))
				reportFile = reportFile + "." + filter;
			reportFile = reportFile + ".html";
			reportFile = Path.Combine(currentSettings.ReportPath, reportFile);
			return reportFile;
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
		public string ReportCSS;
		public int TimePeriod;
		public string WhereTime;

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
			iniParser.CommentDelimiter = '#';
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
			ReportCSS = "";
			try
			{
				ReportCSS = iniData["General"]["ReportCSS"];
			}
			catch {}
			string sTimePeriod = "";
			try
			{
				sTimePeriod = iniData["General"]["TimePeriod"];
			}
			catch { }
			if (String.IsNullOrWhiteSpace(sTimePeriod))
			{
				TimePeriod = 24;
			}
			else
			{
				try
				{
					TimePeriod = Convert.ToInt32(sTimePeriod);
				}
				catch
				{
					TimePeriod = 24;
				}
			}
			Program.log.Info(String.Format("Get report for last {0} hours", TimePeriod.ToString()));
			WhereTime = DateTime.UtcNow.AddHours(-TimePeriod).ToString("yyyyMMdd HH:mm:ss");
		}
	}

}
