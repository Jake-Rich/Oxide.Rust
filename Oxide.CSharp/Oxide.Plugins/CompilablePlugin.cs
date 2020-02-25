using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Reflection;

namespace Oxide.Plugins
{
	public class CompilablePlugin : CompilableFile
	{
		private static object compileLock = new object();

		public CompiledAssembly LastGoodAssembly;

		public bool IsLoading;

		public CompilablePlugin(CSharpExtension extension, CSharpPluginLoader loader, string directory, string name)
			: base(extension, loader, directory, name)
		{
		}

		protected override void OnLoadingStarted()
		{
			Loader.PluginLoadingStarted(this);
		}

		protected override void OnCompilationRequested()
		{
			Loader.CompilationRequested(this);
		}

		internal void LoadPlugin(Action<CSharpPlugin> callback = null)
		{
			if (CompiledAssembly == null)
			{
				Interface.Oxide.LogError("Load called before a compiled assembly exists: {0}", Name);
				return;
			}
			LoadCallback = callback;
			CompiledAssembly.LoadAssembly(delegate(bool loaded)
			{
				if (!loaded)
				{
					callback?.Invoke(null);
				}
				else if (CompilerErrors != null)
				{
					InitFailed("Unable to load " + ScriptName + ". " + CompilerErrors);
				}
				else
				{
					Type type = CompiledAssembly.LoadedAssembly.GetType("Oxide.Plugins." + Name);
					if (type == null)
					{
						InitFailed("Unable to find main plugin class: " + Name);
					}
					else
					{
						CSharpPlugin cSharpPlugin;
						try
						{
							cSharpPlugin = (Activator.CreateInstance(type) as CSharpPlugin);
						}
						catch (MissingMethodException)
						{
							InitFailed("Main plugin class should not have a constructor defined: " + Name);
							return;
						}
						catch (TargetInvocationException ex2)
						{
							Exception innerException = ex2.InnerException;
							InitFailed("Unable to load " + ScriptName + ". " + innerException.ToString());
							return;
						}
						catch (Exception ex3)
						{
							InitFailed("Unable to load " + ScriptName + ". " + ex3.ToString());
							return;
						}
						if (cSharpPlugin == null)
						{
							InitFailed("Plugin assembly failed to load: " + ScriptName);
						}
						else if (!cSharpPlugin.SetPluginInfo(ScriptName, ScriptPath))
						{
							InitFailed();
						}
						else
						{
							cSharpPlugin.Watcher = Extension.Watcher;
							cSharpPlugin.Loader = Loader;
							if (!Interface.Oxide.PluginLoaded(cSharpPlugin))
							{
								InitFailed();
							}
							else
							{
								if (!CompiledAssembly.IsBatch)
								{
									LastGoodAssembly = CompiledAssembly;
								}
								callback?.Invoke(cSharpPlugin);
							}
						}
					}
				}
			});
		}

		internal override void OnCompilationStarted()
		{
			base.OnCompilationStarted();
			foreach (Plugin plugin in Interface.Oxide.RootPluginManager.GetPlugins())
			{
				if (plugin is CSharpPlugin)
				{
					CompilablePlugin compilablePlugin = CSharpPluginLoader.GetCompilablePlugin(Directory, plugin.Name);
					if (compilablePlugin.Requires.Contains(Name))
					{
						compilablePlugin.CompiledAssembly = null;
						Loader.Load(compilablePlugin);
					}
				}
			}
		}

		protected override void InitFailed(string message = null)
		{
			base.InitFailed(message);
			if (LastGoodAssembly == null)
			{
				Interface.Oxide.LogInfo("No previous version to rollback plugin: {0}", ScriptName);
				return;
			}
			if (CompiledAssembly == LastGoodAssembly)
			{
				Interface.Oxide.LogInfo("Previous version of plugin failed to load: {0}", ScriptName);
				return;
			}
			Interface.Oxide.LogInfo("Rolling back plugin to last good version: {0}", ScriptName);
			CompiledAssembly = LastGoodAssembly;
			CompilerErrors = null;
			LoadPlugin();
		}
	}
}
