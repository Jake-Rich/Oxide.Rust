using Oxide.Core;
using Oxide.Core.Extensions;
using Oxide.Core.Plugins;
using Oxide.Core.Plugins.Watchers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Oxide.Plugins
{
	public class CSharpExtension : Extension
	{
		internal static Assembly Assembly = Assembly.GetExecutingAssembly();

		internal static AssemblyName AssemblyName = Assembly.GetName();

		internal static VersionNumber AssemblyVersion = new VersionNumber(AssemblyName.Version.Major, AssemblyName.Version.Minor, AssemblyName.Version.Build);

		internal static string AssemblyAuthors = ((AssemblyCompanyAttribute)Attribute.GetCustomAttribute(Assembly, typeof(AssemblyCompanyAttribute), inherit: false)).Company;

		private CSharpPluginLoader loader;

		public override bool IsCoreExtension => true;

		public override string Name => "CSharp";

		public override string Author => AssemblyAuthors;

		public override VersionNumber Version => AssemblyVersion;

		public FSWatcher Watcher
		{
			get;
			private set;
		}

		public CSharpExtension(ExtensionManager manager)
			: base(manager)
		{
			if (Environment.OSVersion.Platform == PlatformID.Unix)
			{
				string extensionDirectory = Interface.Oxide.ExtensionDirectory;
				string path = Path.Combine(extensionDirectory, "Oxide.References.dll.config");
				if (!File.Exists(path) || new string[2]
				{
					"target=\"x64",
					"target=\"./x64"
				}.Any(File.ReadAllText(path).Contains))
				{
					File.WriteAllText(path, "<configuration>\n<dllmap dll=\"MonoPosixHelper\" target=\"" + extensionDirectory + "/x86/libMonoPosixHelper.so\" os=\"!windows,osx\" wordsize=\"32\" />\n<dllmap dll=\"MonoPosixHelper\" target=\"" + extensionDirectory + "/x64/libMonoPosixHelper.so\" os=\"!windows,osx\" wordsize=\"64\" />\n</configuration>");
				}
			}
		}

		public override void Load()
		{
			loader = new CSharpPluginLoader(this);
			base.Manager.RegisterPluginLoader(loader);
			Interface.Oxide.OnFrame(OnFrame);
		}

		public override void LoadPluginWatchers(string pluginDirectory)
		{
			Watcher = new FSWatcher(pluginDirectory, "*.cs");
			base.Manager.RegisterPluginChangeWatcher(Watcher);
		}

		public override void OnModLoad()
		{
			loader.OnModLoaded();
		}

		public override void OnShutdown()
		{
			base.OnShutdown();
			loader.OnShutdown();
		}

		private void OnFrame(float delta)
		{
			object[] args = new object[1]
			{
				delta
			};
			foreach (KeyValuePair<string, Plugin> loadedPlugin in loader.LoadedPlugins)
			{
				CSharpPlugin cSharpPlugin = loadedPlugin.Value as CSharpPlugin;
				if (cSharpPlugin != null && cSharpPlugin.HookedOnFrame)
				{
					cSharpPlugin.CallHook("OnFrame", args);
				}
			}
		}
	}
}
