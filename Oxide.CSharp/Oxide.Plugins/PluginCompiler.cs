using Mono.Unix;
using Mono.Unix.Native;
using ObjectStream;
using ObjectStream.Data;
using Oxide.Core;
using Oxide.Core.Libraries;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace Oxide.Plugins
{
	public class PluginCompiler
	{
		private static class Algorithms
		{
			public static readonly HashAlgorithm MD5 = new MD5CryptoServiceProvider();

			public static readonly HashAlgorithm SHA1 = new SHA1Managed();

			public static readonly HashAlgorithm SHA256 = new SHA256Managed();

			public static readonly HashAlgorithm SHA384 = new SHA384Managed();

			public static readonly HashAlgorithm SHA512 = new SHA512Managed();

			public static readonly HashAlgorithm RIPEMD160 = new RIPEMD160Managed();
		}

		public static bool AutoShutdown = true;

		public static bool TraceRan;

		public static string FileName = "basic.exe";

		public static string BinaryPath;

		public static string CompilerVersion;

		private static int downloadRetries = 0;

		private Process process;

		private readonly Regex fileErrorRegex = new Regex("([\\w\\.]+)\\(\\d+\\,\\d+\\+?\\): error|error \\w+: Source file `[\\\\\\./]*([\\w\\.]+)", RegexOptions.Compiled);

		private ObjectStreamClient<CompilerMessage> client;

		private Hash<int, Compilation> compilations;

		private Queue<CompilerMessage> messageQueue;

		private volatile int lastId;

		private volatile bool ready;

		private Oxide.Core.Libraries.Timer.TimerInstance idleTimer;

		public static void CheckCompilerBinary()
		{
			BinaryPath = null;
			string rootDirectory = Interface.Oxide.RootDirectory;
			string text = Path.Combine(rootDirectory, FileName);
			if (File.Exists(text))
			{
				BinaryPath = text;
				return;
			}
			switch (Environment.OSVersion.Platform)
			{
			case PlatformID.Win32S:
			case PlatformID.Win32Windows:
			case PlatformID.Win32NT:
				FileName = "Compiler.exe";
				text = Path.Combine(rootDirectory, FileName);
				UpdateCheck();
				break;
			case PlatformID.Unix:
			case PlatformID.MacOSX:
				FileName = "Compiler." + ((IntPtr.Size != 8) ? "x86" : "x86_x64");
				text = Path.Combine(rootDirectory, FileName);
				UpdateCheck();
				try
				{
					if (Syscall.access(text, AccessModes.X_OK) == 0)
					{
						break;
					}
				}
				catch (Exception ex)
				{
					Interface.Oxide.LogError("Unable to check " + FileName + " for executable permission");
					Interface.Oxide.LogError(ex.Message);
					Interface.Oxide.LogError(ex.StackTrace);
				}
				try
				{
					Syscall.chmod(text, FilePermissions.S_IRWXU);
				}
				catch (Exception ex2)
				{
					Interface.Oxide.LogError("Could not set " + FileName + " as executable, please set manually");
					Interface.Oxide.LogError(ex2.Message);
					Interface.Oxide.LogError(ex2.StackTrace);
				}
				break;
			}
			BinaryPath = text;
		}

		private void DependencyTrace()
		{
			if (!TraceRan && Environment.OSVersion.Platform == PlatformID.Unix)
			{
				try
				{
					Interface.Oxide.LogWarning("Running dependency trace for " + FileName);
					Process obj = new Process
					{
						StartInfo = 
						{
							WorkingDirectory = Interface.Oxide.RootDirectory,
							FileName = "/bin/bash",
							Arguments = "-c \"LD_TRACE_LOADED_OBJECTS=1 " + BinaryPath + "\"",
							CreateNoWindow = true,
							UseShellExecute = false,
							RedirectStandardInput = true,
							RedirectStandardOutput = true
						},
						EnableRaisingEvents = true
					};
					string environmentVariable = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
					Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", environmentVariable + ":" + Path.Combine(Interface.Oxide.ExtensionDirectory, (IntPtr.Size == 8) ? "x64" : "x86"));
					obj.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = Path.Combine(Interface.Oxide.ExtensionDirectory, (IntPtr.Size == 8) ? "x64" : "x86");
					obj.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
					{
						Interface.Oxide.LogError(e.Data.TrimStart());
					};
					obj.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
					{
						Interface.Oxide.LogError(e.Data.TrimStart());
					};
					obj.Start();
					obj.BeginOutputReadLine();
					obj.BeginErrorReadLine();
					obj.WaitForExit();
				}
				catch (Exception)
				{
				}
				TraceRan = true;
			}
		}

		private static void DownloadCompiler(string remoteHash)
		{
			try
			{
				Interface.Oxide.LogInfo("Downloading " + FileName + " for .cs (C#) plugin compilation");
				HttpWebResponse httpWebResponse = (HttpWebResponse)((HttpWebRequest)WebRequest.Create("https://assets.umod.org/compiler/" + FileName)).GetResponse();
				int statusCode = (int)httpWebResponse.StatusCode;
				if (statusCode != 200)
				{
					Interface.Oxide.LogWarning($"Status code for compiler download was not okay (code {statusCode})");
				}
				FileStream fileStream = new FileStream(FileName, FileMode.Create, FileAccess.Write, FileShare.None);
				Stream responseStream = httpWebResponse.GetResponseStream();
				int num = 10000;
				byte[] buffer = new byte[num];
				while (true)
				{
					int num2 = responseStream.Read(buffer, 0, num);
					if (num2 == -1 || num2 == 0)
					{
						break;
					}
					fileStream.Write(buffer, 0, num2);
				}
				fileStream.Flush();
				fileStream.Close();
				responseStream.Close();
				httpWebResponse.Close();
				if (downloadRetries >= 2)
				{
					Interface.Oxide.LogInfo("Couldn not download " + FileName + "! Please download manually from: https://assets.umod.org/compiler/" + FileName);
				}
				else
				{
					string b = File.Exists(BinaryPath) ? GetHash(BinaryPath, Algorithms.MD5) : "0";
					if (remoteHash != b)
					{
						Interface.Oxide.LogInfo("Local MD5 hash did not match remote MD5 hash for " + FileName + ", attempting download again");
						downloadRetries++;
						UpdateCheck();
					}
					else
					{
						Interface.Oxide.LogInfo("Download of " + FileName + " completed successfully");
					}
				}
			}
			catch (Exception ex)
			{
				Interface.Oxide.LogError("Could not download " + FileName + "! Please download manually from: https://assets.umod.org/compiler/" + FileName);
				Interface.Oxide.LogError(ex.Message);
			}
		}

		private static void UpdateCheck()
		{
			try
			{
				string text = Path.Combine(Interface.Oxide.RootDirectory, FileName);
				HttpWebResponse httpWebResponse = (HttpWebResponse)((HttpWebRequest)WebRequest.Create("https://assets.umod.org/compiler/" + FileName + ".md5")).GetResponse();
				int statusCode = (int)httpWebResponse.StatusCode;
				if (statusCode != 200)
				{
					Interface.Oxide.LogWarning($"Status code for compiler update check was not okay (code {statusCode})");
				}
				string text2 = "0";
				string text3 = "0";
				Stream responseStream = httpWebResponse.GetResponseStream();
				using (StreamReader streamReader = new StreamReader(responseStream))
				{
					text2 = streamReader.ReadToEnd().Trim().ToLowerInvariant();
					text3 = (File.Exists(text) ? GetHash(text, Algorithms.MD5) : "0");
					Interface.Oxide.LogInfo("Latest compiler MD5: " + text2);
					Interface.Oxide.LogInfo("Local compiler MD5: " + text3);
				}
				responseStream.Close();
				httpWebResponse.Close();
				if (text2 != text3)
				{
					Interface.Oxide.LogInfo("Compiler MD5 hash did not match, downloading latest");
					DownloadCompiler(text2);
				}
			}
			catch (Exception ex)
			{
				Interface.Oxide.LogError("Could not check for update to " + FileName);
				Interface.Oxide.LogError(ex.Message);
			}
		}

		private static void SetCompilerVersion()
		{
			CompilerVersion = (File.Exists(BinaryPath) ? FileVersionInfo.GetVersionInfo(BinaryPath).FileVersion : "Unknown");
			RemoteLogger.SetTag("compiler version", CompilerVersion);
		}

		public PluginCompiler()
		{
			compilations = new Hash<int, Compilation>();
			messageQueue = new Queue<CompilerMessage>();
		}

		internal void Compile(CompilablePlugin[] plugins, Action<Compilation> callback)
		{
			int num = lastId++;
			Compilation compilation = new Compilation(num, callback, plugins);
			compilations[num] = compilation;
			compilation.Prepare(delegate
			{
				EnqueueCompilation(compilation);
			});
		}

		public void Shutdown()
		{
			ready = false;
			Process endedProcess = process;
			if (endedProcess != null)
			{
				endedProcess.Exited -= OnProcessExited;
			}
			process = null;
			if (client != null)
			{
				client.Message -= OnMessage;
				client.Error -= OnError;
				client.PushMessage(new CompilerMessage
				{
					Type = CompilerMessageType.Exit
				});
				client.Stop();
				client = null;
				if (endedProcess != null)
				{
					ThreadPool.QueueUserWorkItem(delegate
					{
						Thread.Sleep(5000);
						if (!endedProcess.HasExited)
						{
							endedProcess.Close();
						}
					});
				}
			}
		}

		private void EnqueueCompilation(Compilation compilation)
		{
			if (compilation.plugins.Count < 1)
			{
				return;
			}
			if (!CheckCompiler())
			{
				OnCompilerFailed("compiler version " + CompilerVersion + " couldn't be started");
				return;
			}
			compilation.Started();
			List<CompilerFile> list = (from path in compilation.plugins.SelectMany((CompilablePlugin plugin) => plugin.IncludePaths).Distinct()
				select new CompilerFile(path)).ToList();
			list.AddRange(compilation.plugins.Select((CompilablePlugin plugin) => new CompilerFile(plugin.ScriptName + ".cs", plugin.ScriptSource)));
			CompilerData data = new CompilerData
			{
				OutputFile = compilation.name,
				SourceFiles = list.ToArray(),
				ReferenceFiles = compilation.references.Values.ToArray()
			};
			CompilerMessage compilerMessage = new CompilerMessage
			{
				Id = compilation.id,
				Data = data,
				Type = CompilerMessageType.Compile
			};
			if (ready)
			{
				client.PushMessage(compilerMessage);
			}
			else
			{
				messageQueue.Enqueue(compilerMessage);
			}
		}

		private void OnMessage(ObjectStreamConnection<CompilerMessage, CompilerMessage> connection, CompilerMessage message)
		{
			if (message == null)
			{
				Interface.Oxide.NextTick(delegate
				{
					OnCompilerFailed("compiler version " + CompilerVersion + " disconnected");
					DependencyTrace();
					Shutdown();
				});
				return;
			}
			switch (message.Type)
			{
			case CompilerMessageType.Compile:
			case CompilerMessageType.Exit:
				break;
			case CompilerMessageType.Assembly:
			{
				Compilation compilation = compilations[message.Id];
				if (compilation == null)
				{
					Interface.Oxide.LogWarning("Compiler compiled an unknown assembly");
					break;
				}
				compilation.endedAt = Interface.Oxide.Now;
				string text = (string)message.ExtraData;
				if (text != null)
				{
					string[] array = text.Split('\r', '\n');
					foreach (string text2 in array)
					{
						Match match = fileErrorRegex.Match(text2.Trim());
						for (int j = 1; j < match.Groups.Count; j++)
						{
							string value = match.Groups[j].Value;
							if (value.Trim() == string.Empty)
							{
								continue;
							}
							string text3 = value.Basename();
							string scriptName = text3.Substring(0, text3.Length - 3);
							CompilablePlugin compilablePlugin = compilation.plugins.SingleOrDefault((CompilablePlugin pl) => pl.ScriptName == scriptName);
							if (compilablePlugin == null)
							{
								Interface.Oxide.LogError("Unable to resolve script error to plugin: " + text2);
								continue;
							}
							IEnumerable<string> enumerable = compilablePlugin.Requires.Where((string name) => !compilation.IncludesRequiredPlugin(name));
							if (enumerable.Any())
							{
								compilablePlugin.CompilerErrors = "Missing dependencies: " + enumerable.ToSentence();
							}
							else
							{
								compilablePlugin.CompilerErrors = text2.Trim().Replace(Interface.Oxide.PluginDirectory + Path.DirectorySeparatorChar, string.Empty);
							}
						}
					}
				}
				compilation.Completed((byte[])message.Data);
				compilations.Remove(message.Id);
				idleTimer?.Destroy();
				if (AutoShutdown)
				{
					Interface.Oxide.NextTick(delegate
					{
						idleTimer?.Destroy();
						if (AutoShutdown)
						{
							idleTimer = Interface.Oxide.GetLibrary<Oxide.Core.Libraries.Timer>().Once(60f, Shutdown);
						}
					});
				}
				break;
			}
			case CompilerMessageType.Error:
				Interface.Oxide.LogError("Compilation error: {0}", message.Data);
				compilations[message.Id].Completed();
				compilations.Remove(message.Id);
				idleTimer?.Destroy();
				if (AutoShutdown)
				{
					Interface.Oxide.NextTick(delegate
					{
						idleTimer?.Destroy();
						idleTimer = Interface.Oxide.GetLibrary<Oxide.Core.Libraries.Timer>().Once(60f, Shutdown);
					});
				}
				break;
			case CompilerMessageType.Ready:
				connection.PushMessage(message);
				if (!ready)
				{
					ready = true;
					while (messageQueue.Count > 0)
					{
						connection.PushMessage(messageQueue.Dequeue());
					}
				}
				break;
			}
		}

		private static void OnError(Exception exception)
		{
			Interface.Oxide.LogException("Compilation error: ", exception);
		}

		private bool CheckCompiler()
		{
			CheckCompilerBinary();
			idleTimer?.Destroy();
			if (BinaryPath == null)
			{
				return false;
			}
			if (process != null && process.Handle != IntPtr.Zero && !process.HasExited)
			{
				return true;
			}
			SetCompilerVersion();
			PurgeOldLogs();
			Shutdown();
			string[] value = new string[2]
			{
				"/service",
				"/logPath:" + EscapePath(Interface.Oxide.LogDirectory)
			};
			try
			{
				process = new Process
				{
					StartInfo = 
					{
						FileName = BinaryPath,
						Arguments = string.Join(" ", value),
						CreateNoWindow = true,
						UseShellExecute = false,
						RedirectStandardInput = true,
						RedirectStandardOutput = true
					},
					EnableRaisingEvents = true
				};
				switch (Environment.OSVersion.Platform)
				{
				case PlatformID.Win32S:
				case PlatformID.Win32Windows:
				case PlatformID.Win32NT:
				{
					string environmentVariable2 = Environment.GetEnvironmentVariable("PATH");
					Environment.SetEnvironmentVariable("PATH", environmentVariable2 + ";" + Path.Combine(Interface.Oxide.ExtensionDirectory, "x86"));
					break;
				}
				case PlatformID.Unix:
				case PlatformID.MacOSX:
				{
					string environmentVariable = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
					process.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = Path.Combine(Interface.Oxide.ExtensionDirectory, (IntPtr.Size == 8) ? "x64" : "x86");
					Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", environmentVariable + ":" + Path.Combine(Interface.Oxide.ExtensionDirectory, (IntPtr.Size == 8) ? "x64" : "x86"));
					break;
				}
				}
				process.Exited += OnProcessExited;
				process.Start();
			}
			catch (Exception ex)
			{
				process?.Dispose();
				process = null;
				Interface.Oxide.LogException("Exception while starting compiler version " + CompilerVersion + ": ", ex);
				if (BinaryPath.Contains("'"))
				{
					Interface.Oxide.LogWarning("Server directory path contains an apostrophe, compiler will not work until path is renamed");
				}
				else if (Environment.OSVersion.Platform == PlatformID.Unix)
				{
					Interface.Oxide.LogWarning("Compiler may not be set as executable; chmod +x or 0744/0755 required");
				}
				if (ex.GetBaseException() != ex)
				{
					Interface.Oxide.LogException("BaseException: ", ex.GetBaseException());
				}
				Win32Exception ex2 = ex as Win32Exception;
				if (ex2 != null)
				{
					Interface.Oxide.LogError("Win32 NativeErrorCode: {0} ErrorCode: {1} HelpLink: {2}", ex2.NativeErrorCode, ex2.ErrorCode, ex2.HelpLink);
				}
			}
			if (process == null)
			{
				return false;
			}
			client = new ObjectStreamClient<CompilerMessage>(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
			client.Message += OnMessage;
			client.Error += OnError;
			client.Start();
			return true;
		}

		private void OnProcessExited(object sender, EventArgs eventArgs)
		{
			Interface.Oxide.NextTick(delegate
			{
				OnCompilerFailed("compiler version " + CompilerVersion + " was closed unexpectedly");
				if (Environment.OSVersion.Platform == PlatformID.Unix)
				{
					string environmentVariable = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
					string text = Path.Combine(Interface.Oxide.ExtensionDirectory, (IntPtr.Size == 8) ? "x64" : "x86");
					if (string.IsNullOrEmpty(environmentVariable) || !environmentVariable.Contains(text))
					{
						Interface.Oxide.LogWarning("LD_LIBRARY_PATH does not container path to compiler dependencies: " + text);
					}
					else
					{
						Interface.Oxide.LogWarning("User running server may not have the proper permissions or install is missing files");
						Interface.Oxide.LogWarning("User running server: " + Environment.UserName);
						UnixFileInfo unixFileInfo = new UnixFileInfo(BinaryPath);
						Interface.Oxide.LogWarning($"Compiler under user/group: {unixFileInfo.OwnerUser}/{unixFileInfo.OwnerGroup}");
						string path = Path.Combine(Interface.Oxide.ExtensionDirectory, (IntPtr.Size == 8) ? "x64" : "x86");
						string[] array = new string[2]
						{
							"libmonoboehm-2.0.so.1",
							"libMonoPosixHelper.so"
						};
						foreach (string path2 in array)
						{
							string text2 = Path.Combine(path, path2);
							if (!File.Exists(text2))
							{
								Interface.Oxide.LogWarning(text2 + " is missing");
							}
						}
					}
				}
				else
				{
					string environmentVariable2 = Environment.GetEnvironmentVariable("PATH");
					string text3 = Path.Combine(Interface.Oxide.ExtensionDirectory, "x86");
					if (string.IsNullOrEmpty(environmentVariable2) || !environmentVariable2.Contains(text3))
					{
						Interface.Oxide.LogWarning("PATH does not container path to compiler dependencies: " + text3);
					}
					else
					{
						Interface.Oxide.LogWarning("Compiler may have been closed by interference from security software or install is missing files");
						string path3 = Path.Combine(Interface.Oxide.ExtensionDirectory, "x86");
						string[] array = new string[3]
						{
							"mono-2.0.dll",
							"msvcp140.dll",
							"msvcr120.dll"
						};
						foreach (string path4 in array)
						{
							string text4 = Path.Combine(path3, path4);
							if (!File.Exists(text4))
							{
								Interface.Oxide.LogWarning(text4 + " is missing");
							}
						}
					}
				}
				Shutdown();
			});
		}

		private void OnCompilerFailed(string reason)
		{
			foreach (Compilation value in compilations.Values)
			{
				foreach (CompilablePlugin plugin in value.plugins)
				{
					plugin.CompilerErrors = reason;
				}
				value.Completed();
			}
			compilations.Clear();
		}

		private static void PurgeOldLogs()
		{
			try
			{
				foreach (string item in from f in Directory.GetFiles(Interface.Oxide.LogDirectory, "*.txt")
					where Path.GetFileName(f)?.StartsWith("compiler_") ?? false
					select f)
				{
					File.Delete(item);
				}
			}
			catch (Exception)
			{
			}
		}

		private static string EscapePath(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return "\"\"";
			}
			path = Regex.Replace(path, "(\\\\*)\"", "$1\\$0");
			path = Regex.Replace(path, "^(.*\\s.*?)(\\\\*)$", "\"$1$2$2\"");
			return path;
		}

		private static string GetHash(string filePath, HashAlgorithm algorithm)
		{
			using (BufferedStream inputStream = new BufferedStream(File.OpenRead(filePath), 100000))
			{
				return BitConverter.ToString(algorithm.ComputeHash(inputStream)).Replace("-", string.Empty).ToLower();
			}
		}
	}
}
