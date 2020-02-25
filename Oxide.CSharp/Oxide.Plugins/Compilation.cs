using ObjectStream.Data;
using Oxide.Core;
using Oxide.Core.Extensions;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace Oxide.Plugins
{
	internal class Compilation
	{
		public static Compilation Current;

		internal int id;

		internal string name;

		internal Action<Compilation> callback;

		internal ConcurrentHashSet<CompilablePlugin> queuedPlugins;

		internal HashSet<CompilablePlugin> plugins = new HashSet<CompilablePlugin>();

		internal float startedAt;

		internal float endedAt;

		internal Hash<string, CompilerFile> references = new Hash<string, CompilerFile>();

		internal HashSet<string> referencedPlugins = new HashSet<string>();

		internal CompiledAssembly compiledAssembly;

		private string includePath;

		private string[] extensionNames;

		private string gameExtensionNamespace;

		private readonly string gameExtensionName;

		private readonly string gameExtensionBranch;

		internal float duration => endedAt - startedAt;

		internal Compilation(int id, Action<Compilation> callback, CompilablePlugin[] plugins)
		{
			this.id = id;
			this.callback = callback;
			queuedPlugins = new ConcurrentHashSet<CompilablePlugin>(plugins);
			if (Current == null)
			{
				Current = this;
			}
			foreach (CompilablePlugin obj in plugins)
			{
				obj.CompilerErrors = null;
				obj.OnCompilationStarted();
			}
			includePath = Path.Combine(Interface.Oxide.PluginDirectory, "include");
			extensionNames = (from ext in Interface.Oxide.GetAllExtensions()
				select ext.Name).ToArray();
			Extension extension = Interface.Oxide.GetAllExtensions().SingleOrDefault((Extension ext) => ext.IsGameExtension);
			gameExtensionName = extension?.Name.ToUpper();
			gameExtensionNamespace = extension?.GetType().Namespace;
			gameExtensionBranch = extension?.Branch?.ToUpper();
		}

		internal void Started()
		{
			startedAt = Interface.Oxide.Now;
			name = ((plugins.Count < 2) ? plugins.First().Name : "plugins_") + Math.Round(Interface.Oxide.Now * 1E+07f) + ".dll";
		}

		internal void Completed(byte[] rawAssembly = null)
		{
			endedAt = Interface.Oxide.Now;
			if (plugins.Count > 0 && rawAssembly != null)
			{
				compiledAssembly = new CompiledAssembly(name, plugins.ToArray(), rawAssembly, duration);
			}
			Interface.Oxide.NextTick(delegate
			{
				callback(this);
			});
		}

		internal void Add(CompilablePlugin plugin)
		{
			if (queuedPlugins.Add(plugin))
			{
				plugin.Loader.PluginLoadingStarted(plugin);
				plugin.CompilerErrors = null;
				plugin.OnCompilationStarted();
				foreach (Plugin item in from pl in Interface.Oxide.RootPluginManager.GetPlugins()
					where pl is CSharpPlugin
					select pl)
				{
					CompilablePlugin compilablePlugin = CSharpPluginLoader.GetCompilablePlugin(plugin.Directory, item.Name);
					if (compilablePlugin.Requires.Contains(plugin.Name))
					{
						AddDependency(compilablePlugin);
					}
				}
			}
		}

		internal bool IncludesRequiredPlugin(string name)
		{
			if (referencedPlugins.Contains(name))
			{
				return true;
			}
			CompilablePlugin compilablePlugin = plugins.SingleOrDefault((CompilablePlugin pl) => pl.Name == name);
			if (compilablePlugin != null)
			{
				return compilablePlugin.CompilerErrors == null;
			}
			return false;
		}

		internal void Prepare(Action callback)
		{
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					referencedPlugins.Clear();
					references.Clear();
					foreach (string pluginReference in CSharpPluginLoader.PluginReferences)
					{
						if (File.Exists(Path.Combine(Interface.Oxide.ExtensionDirectory, pluginReference + ".dll")))
						{
							references[pluginReference + ".dll"] = new CompilerFile(Interface.Oxide.ExtensionDirectory, pluginReference + ".dll");
						}
						if (File.Exists(Path.Combine(Interface.Oxide.ExtensionDirectory, pluginReference + ".exe")))
						{
							references[pluginReference + ".exe"] = new CompilerFile(Interface.Oxide.ExtensionDirectory, pluginReference + ".exe");
						}
					}
					CompilablePlugin value;
					while (queuedPlugins.TryDequeue(out value))
					{
						if (Current == null)
						{
							Current = this;
						}
						if (!CacheScriptLines(value) || value.ScriptLines.Length < 1)
						{
							value.References.Clear();
							value.IncludePaths.Clear();
							value.Requires.Clear();
							Interface.Oxide.LogWarning("Plugin script is empty: " + value.Name);
							RemovePlugin(value);
						}
						else if (plugins.Add(value))
						{
							PreparseScript(value);
							ResolveReferences(value);
						}
						CacheModifiedScripts();
						if (queuedPlugins.Count == 0 && Current == this)
						{
							Current = null;
						}
					}
					callback();
				}
				catch (Exception ex)
				{
					Interface.Oxide.LogException("Exception while resolving plugin references", ex);
				}
			});
		}

		private void PreparseScript(CompilablePlugin plugin)
		{
			plugin.References.Clear();
			plugin.IncludePaths.Clear();
			plugin.Requires.Clear();
			bool flag = false;
			int num = 0;
			string value2;
			while (true)
			{
				if (num >= plugin.ScriptLines.Length)
				{
					return;
				}
				string text = plugin.ScriptLines[num].Trim();
				if (text.Length >= 1)
				{
					if (flag)
					{
						Match match = Regex.Match(text, "^\\s*\\{?\\s*$", RegexOptions.IgnoreCase);
						if (!match.Success)
						{
							match = Regex.Match(text, "^\\s*\\[", RegexOptions.IgnoreCase);
							if (!match.Success)
							{
								match = Regex.Match(text, "^\\s*(?:public|private|protected|internal)?\\s*class\\s+(\\S+)\\s+\\:\\s+\\S+Plugin\\s*$", RegexOptions.IgnoreCase);
								if (match.Success)
								{
									string value = match.Groups[1].Value;
									if (value != plugin.Name)
									{
										Interface.Oxide.LogError("Plugin filename " + plugin.ScriptName + ".cs must match the main class " + value + " (should be " + value + ".cs)");
										plugin.CompilerErrors = "Plugin filename " + plugin.ScriptName + ".cs must match the main class " + value + " (should be " + value + ".cs)";
										RemovePlugin(plugin);
									}
								}
								return;
							}
						}
					}
					else
					{
						Match match = Regex.Match(text, "^//\\s*Requires:\\s*(\\S+?)(\\.cs)?\\s*$", RegexOptions.IgnoreCase);
						if (match.Success)
						{
							value2 = match.Groups[1].Value;
							plugin.Requires.Add(value2);
							if (!File.Exists(Path.Combine(plugin.Directory, value2 + ".cs")))
							{
								break;
							}
							CompilablePlugin compilablePlugin = CSharpPluginLoader.GetCompilablePlugin(plugin.Directory, value2);
							AddDependency(compilablePlugin);
						}
						else
						{
							match = Regex.Match(text, "^//\\s*Reference:\\s*(\\S+)\\s*$", RegexOptions.IgnoreCase);
							if (match.Success)
							{
								string value3 = match.Groups[1].Value;
								if (!value3.StartsWith("Oxide.") && !value3.StartsWith("Newtonsoft.Json") && !value3.StartsWith("protobuf-net") && !value3.StartsWith("Rust."))
								{
									AddReference(plugin, value3);
									Interface.Oxide.LogInfo("Added '// Reference: {0}' in plugin '{1}'", value3, plugin.Name);
								}
								else
								{
									Interface.Oxide.LogWarning("Ignored unnecessary '// Reference: {0}' in plugin '{1}'", value3, plugin.Name);
								}
							}
							else
							{
								match = Regex.Match(text, "^\\s*using\\s+(Oxide\\.(?:Core|Ext|Game)\\.(?:[^\\.]+))[^;]*;.*$", RegexOptions.IgnoreCase);
								if (match.Success)
								{
									string value4 = match.Groups[1].Value;
									string text2 = Regex.Replace(value4, "Oxide\\.[\\w]+\\.([\\w]+)", "Oxide.$1");
									if (!string.IsNullOrEmpty(text2) && File.Exists(Path.Combine(Interface.Oxide.ExtensionDirectory, text2 + ".dll")))
									{
										AddReference(plugin, text2);
									}
									else
									{
										AddReference(plugin, value4);
									}
								}
								else
								{
									match = Regex.Match(text, "^\\s*namespace Oxide\\.Plugins\\s*(\\{\\s*)?$", RegexOptions.IgnoreCase);
									if (match.Success)
									{
										flag = true;
									}
								}
							}
						}
					}
				}
				num++;
			}
			Interface.Oxide.LogError(plugin.Name + " plugin requires missing dependency: " + value2);
			plugin.CompilerErrors = "Missing dependency: " + value2;
			RemovePlugin(plugin);
		}

		private void ResolveReferences(CompilablePlugin plugin)
		{
			foreach (string reference in plugin.References)
			{
				Match match = Regex.Match(reference, "^(Oxide\\.(?:Ext|Game)\\.(.+))$", RegexOptions.IgnoreCase);
				if (match.Success)
				{
					string value = match.Groups[1].Value;
					string value2 = match.Groups[2].Value;
					if (!extensionNames.Contains(value2))
					{
						if (Directory.Exists(includePath))
						{
							string text = Path.Combine(includePath, "Ext." + value2 + ".cs");
							if (File.Exists(text))
							{
								plugin.IncludePaths.Add(text);
								continue;
							}
						}
						string text2 = value + " is referenced by " + plugin.Name + " plugin but is not loaded! An appropriate include file needs to be saved to plugins\\include\\Ext." + value2 + ".cs if this extension is not required.";
						Interface.Oxide.LogError(text2);
						plugin.CompilerErrors = text2;
						RemovePlugin(plugin);
					}
				}
			}
		}

		private void AddDependency(CompilablePlugin plugin)
		{
			if (plugin.IsLoading || plugins.Contains(plugin) || queuedPlugins.Contains(plugin))
			{
				return;
			}
			CompiledAssembly compiledAssembly = plugin.CompiledAssembly;
			if (compiledAssembly != null && !compiledAssembly.IsOutdated())
			{
				referencedPlugins.Add(plugin.Name);
				if (!references.ContainsKey(compiledAssembly.Name))
				{
					references[compiledAssembly.Name] = new CompilerFile(compiledAssembly.Name, compiledAssembly.RawAssembly);
				}
			}
			else
			{
				Add(plugin);
			}
		}

		private void AddReference(CompilablePlugin plugin, string assemblyName)
		{
			if (!File.Exists(Path.Combine(Interface.Oxide.ExtensionDirectory, assemblyName + ".dll")))
			{
				if (assemblyName.StartsWith("Oxide."))
				{
					plugin.References.Add(assemblyName);
					return;
				}
				Interface.Oxide.LogError("Assembly referenced by " + plugin.Name + " plugin does not exist: " + assemblyName + ".dll");
				plugin.CompilerErrors = "Referenced assembly does not exist: " + assemblyName;
				RemovePlugin(plugin);
				return;
			}
			Assembly assembly;
			try
			{
				assembly = Assembly.Load(assemblyName);
			}
			catch (FileNotFoundException)
			{
				Interface.Oxide.LogError("Assembly referenced by " + plugin.Name + " plugin is invalid: " + assemblyName + ".dll");
				plugin.CompilerErrors = "Referenced assembly is invalid: " + assemblyName;
				RemovePlugin(plugin);
				return;
			}
			AddReference(plugin, assembly.GetName());
			AssemblyName[] referencedAssemblies = assembly.GetReferencedAssemblies();
			foreach (AssemblyName assemblyName2 in referencedAssemblies)
			{
				if (!assemblyName2.Name.StartsWith("Newtonsoft.Json") && !assemblyName2.Name.StartsWith("Rust.Workshop"))
				{
					if (!File.Exists(Path.Combine(Interface.Oxide.ExtensionDirectory, assemblyName2.Name + ".dll")))
					{
						Interface.Oxide.LogWarning("Reference " + assemblyName2.Name + ".dll from " + assembly.GetName().Name + ".dll not found");
					}
					else
					{
						AddReference(plugin, assemblyName2);
					}
				}
			}
		}

		private void AddReference(CompilablePlugin plugin, AssemblyName reference)
		{
			string key = reference.Name + ".dll";
			if (!references.ContainsKey(key))
			{
				references[key] = new CompilerFile(Interface.Oxide.ExtensionDirectory, key);
			}
			plugin.References.Add(reference.Name);
		}

		private bool CacheScriptLines(CompilablePlugin plugin)
		{
			bool flag = false;
			while (true)
			{
				try
				{
					if (!File.Exists(plugin.ScriptPath))
					{
						Interface.Oxide.LogWarning("Script no longer exists: {0}", plugin.Name);
						plugin.CompilerErrors = "Plugin file was deleted";
						RemovePlugin(plugin);
						return false;
					}
					plugin.CheckLastModificationTime();
					if (plugin.LastCachedScriptAt != plugin.LastModifiedAt)
					{
						using (StreamReader streamReader = File.OpenText(plugin.ScriptPath))
						{
							List<string> list = new List<string>();
							while (!streamReader.EndOfStream)
							{
								list.Add(streamReader.ReadLine());
							}
							if (!string.IsNullOrEmpty(gameExtensionName))
							{
								list.Insert(0, "#define " + gameExtensionName);
							}
							if (!string.IsNullOrEmpty(gameExtensionName))
							{
								list.Insert(0, "#define " + gameExtensionName);
								if (!string.IsNullOrEmpty(gameExtensionBranch) && gameExtensionBranch != "public")
								{
									list.Insert(0, "#define " + gameExtensionName + gameExtensionBranch);
								}
							}
							plugin.ScriptLines = list.ToArray();
							plugin.ScriptEncoding = streamReader.CurrentEncoding;
						}
						plugin.LastCachedScriptAt = plugin.LastModifiedAt;
						if (plugins.Remove(plugin))
						{
							queuedPlugins.Add(plugin);
						}
					}
					return true;
				}
				catch (IOException)
				{
					if (!flag)
					{
						flag = true;
						Interface.Oxide.LogWarning("Waiting for another application to stop using script: {0}", plugin.Name);
					}
					Thread.Sleep(50);
				}
			}
		}

		private void CacheModifiedScripts()
		{
			CompilablePlugin[] array = plugins.Where((CompilablePlugin pl) => pl.ScriptLines == null || pl.HasBeenModified() || pl.LastCachedScriptAt != pl.LastModifiedAt).ToArray();
			if (array.Length >= 1)
			{
				CompilablePlugin[] array2 = array;
				foreach (CompilablePlugin plugin in array2)
				{
					CacheScriptLines(plugin);
				}
				Thread.Sleep(100);
				CacheModifiedScripts();
			}
		}

		private void RemovePlugin(CompilablePlugin plugin)
		{
			if (plugin.LastCompiledAt == default(DateTime))
			{
				return;
			}
			queuedPlugins.Remove(plugin);
			plugins.Remove(plugin);
			plugin.OnCompilationFailed();
			CompilablePlugin[] array = plugins.Where((CompilablePlugin pl) => !pl.IsCompilationNeeded && plugin.Requires.Contains(pl.Name)).ToArray();
			foreach (CompilablePlugin requiredPlugin in array)
			{
				if (!plugins.Any((CompilablePlugin pl) => pl.Requires.Contains(requiredPlugin.Name)))
				{
					RemovePlugin(requiredPlugin);
				}
			}
		}
	}
}
