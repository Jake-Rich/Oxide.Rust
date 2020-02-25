using Oxide.Core;
using Oxide.Core.Extensions;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
	public class CSharpPluginLoader : PluginLoader
	{
		public static string[] DefaultReferences = new string[6]
		{
			"mscorlib",
			"Oxide.Core",
			"Oxide.CSharp",
			"System",
			"System.Core",
			"System.Data"
		};

		public static HashSet<string> PluginReferences = new HashSet<string>(DefaultReferences);

		public static CSharpPluginLoader Instance;

		private static CSharpExtension extension;

		private static Dictionary<string, CompilablePlugin> plugins = new Dictionary<string, CompilablePlugin>();

		private static readonly string[] AssemblyBlacklist = new string[3]
		{
			"Newtonsoft.Json",
			"protobuf-net",
			"websocket-sharp"
		};

		private List<CompilablePlugin> compilationQueue = new List<CompilablePlugin>();

		private PluginCompiler compiler;

		public override string FileExtension => ".cs";

		public static CompilablePlugin GetCompilablePlugin(string directory, string name)
		{
			string key = Regex.Replace(name, "_", "");
			if (!plugins.TryGetValue(key, out CompilablePlugin value))
			{
				value = new CompilablePlugin(extension, Instance, directory, name);
				plugins[key] = value;
			}
			return value;
		}

		public CSharpPluginLoader(CSharpExtension extension)
		{
			Instance = this;
			CSharpPluginLoader.extension = extension;
			compiler = new PluginCompiler();
		}

		public void OnModLoaded()
		{
			PluginCompiler.CheckCompilerBinary();
			foreach (Extension allExtension in Interface.Oxide.GetAllExtensions())
			{
				if (allExtension != null && (allExtension.IsCoreExtension || allExtension.IsGameExtension))
				{
					Assembly assembly = allExtension.GetType().Assembly;
					string name = assembly.GetName().Name;
					if (!AssemblyBlacklist.Contains(name))
					{
						PluginReferences.Add(name);
						AssemblyName[] referencedAssemblies = assembly.GetReferencedAssemblies();
						foreach (AssemblyName assemblyName in referencedAssemblies)
						{
							if (assemblyName != null)
							{
								PluginReferences.Add(assemblyName.Name);
							}
						}
					}
				}
			}
		}

		public override IEnumerable<string> ScanDirectory(string directory)
		{
			if (PluginCompiler.BinaryPath != null)
			{
				IEnumerable<string> enumerable = base.ScanDirectory(directory);
				foreach (string item in enumerable)
				{
					yield return item;
				}
			}
		}

		public override Plugin Load(string directory, string name)
		{
			CompilablePlugin compilablePlugin = GetCompilablePlugin(directory, name);
			if (compilablePlugin.IsLoading)
			{
				Interface.Oxide.LogDebug("Load requested for plugin which is already loading: " + compilablePlugin.Name);
				return null;
			}
			Load(compilablePlugin);
			return null;
		}

		public override void Reload(string directory, string name)
		{
			if (Regex.Match(directory, "\\\\include\\b", RegexOptions.IgnoreCase).Success)
			{
				name = "Oxide." + name;
				foreach (CompilablePlugin value in plugins.Values)
				{
					if (value.References.Contains(name))
					{
						Interface.Oxide.LogInfo("Reloading " + value.Name + " because it references updated include file: " + name);
						value.LastModifiedAt = DateTime.Now;
						Load(value);
					}
				}
				return;
			}
			CompilablePlugin compilablePlugin = GetCompilablePlugin(directory, name);
			if (compilablePlugin.IsLoading)
			{
				Interface.Oxide.LogDebug("Reload requested for plugin which is already loading: " + compilablePlugin.Name);
			}
			else
			{
				Load(compilablePlugin);
			}
		}

		public override void Unloading(Plugin pluginBase)
		{
			CSharpPlugin cSharpPlugin = pluginBase as CSharpPlugin;
			if (cSharpPlugin != null)
			{
				LoadedPlugins.Remove(cSharpPlugin.Name);
				foreach (CompilablePlugin value in plugins.Values)
				{
					if (value.Requires.Contains(cSharpPlugin.Name))
					{
						Interface.Oxide.UnloadPlugin(value.Name);
					}
				}
			}
		}

		public void Load(CompilablePlugin plugin)
		{
			plugin.Compile(delegate(bool compiled)
			{
				if (!compiled)
				{
					PluginLoadingCompleted(plugin);
				}
				else
				{
					foreach (string item in plugin.Requires.Where((string r) => LoadedPlugins.ContainsKey(r) && base.LoadingPlugins.Contains(r)))
					{
						Interface.Oxide.UnloadPlugin(item);
					}
					IEnumerable<string> enumerable = plugin.Requires.Where((string r) => !LoadedPlugins.ContainsKey(r));
					if (enumerable.Any())
					{
						IEnumerable<string> enumerable2 = plugin.Requires.Where((string r) => base.LoadingPlugins.Contains(r));
						if (enumerable2.Any())
						{
							Interface.Oxide.LogDebug(plugin.Name + " plugin is waiting for requirements to be loaded: " + enumerable2.ToSentence());
						}
						else
						{
							Interface.Oxide.LogError(plugin.Name + " plugin requires missing dependencies: " + enumerable.ToSentence());
							base.PluginErrors[plugin.Name] = "Missing dependencies: " + enumerable.ToSentence();
							PluginLoadingCompleted(plugin);
						}
					}
					else
					{
						Interface.Oxide.UnloadPlugin(plugin.Name);
						plugin.LoadPlugin(delegate(CSharpPlugin pl)
						{
							if (pl != null)
							{
								LoadedPlugins[pl.Name] = pl;
							}
							PluginLoadingCompleted(plugin);
						});
					}
				}
			});
		}

		public void CompilationRequested(CompilablePlugin plugin)
		{
			if (Compilation.Current != null)
			{
				Compilation.Current.Add(plugin);
				return;
			}
			if (compilationQueue.Count < 1)
			{
				Interface.Oxide.NextTick(delegate
				{
					CompileAssembly(compilationQueue.ToArray());
					compilationQueue.Clear();
				});
			}
			compilationQueue.Add(plugin);
		}

		public void PluginLoadingStarted(CompilablePlugin plugin)
		{
			base.LoadingPlugins.Add(plugin.Name);
			plugin.IsLoading = true;
		}

		private void PluginLoadingCompleted(CompilablePlugin plugin)
		{
			base.LoadingPlugins.Remove(plugin.Name);
			plugin.IsLoading = false;
			string[] array = base.LoadingPlugins.ToArray();
			foreach (string name in array)
			{
				CompilablePlugin compilablePlugin = GetCompilablePlugin(plugin.Directory, name);
				if (compilablePlugin.IsLoading && compilablePlugin.Requires.Contains(plugin.Name))
				{
					Load(compilablePlugin);
				}
			}
		}

		private void CompileAssembly(CompilablePlugin[] plugins)
		{
			compiler.Compile(plugins, delegate(Compilation compilation)
			{
				if (compilation.compiledAssembly == null)
				{
					foreach (CompilablePlugin plugin in compilation.plugins)
					{
						plugin.OnCompilationFailed();
						base.PluginErrors[plugin.Name] = "Failed to compile: " + plugin.CompilerErrors;
						Interface.Oxide.LogError("Error while compiling: " + plugin.CompilerErrors);
					}
				}
				else
				{
					if (compilation.plugins.Count > 0)
					{
						string[] array = (from pl in compilation.plugins
							where string.IsNullOrEmpty(pl.CompilerErrors)
							select pl.Name).ToArray();
						string arg = (array.Length > 1) ? "were" : "was";
						Interface.Oxide.LogInfo($"{array.ToSentence()} {arg} compiled successfully in {Math.Round(compilation.duration * 1000f)}ms");
					}
					foreach (CompilablePlugin plugin2 in compilation.plugins)
					{
						if (plugin2.CompilerErrors == null)
						{
							Interface.Oxide.UnloadPlugin(plugin2.Name);
							plugin2.OnCompilationSucceeded(compilation.compiledAssembly);
						}
						else
						{
							plugin2.OnCompilationFailed();
							base.PluginErrors[plugin2.Name] = "Failed to compile: " + plugin2.CompilerErrors;
							Interface.Oxide.LogError("Error while compiling: " + plugin2.CompilerErrors);
						}
					}
				}
			});
		}

		public void OnShutdown()
		{
			compiler.Shutdown();
		}
	}
}
