using Oxide.Core;
using Oxide.Core.Libraries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
	public class CompilableFile
	{
		private static Oxide.Core.Libraries.Timer timer = Interface.Oxide.GetLibrary<Oxide.Core.Libraries.Timer>();

		private static object compileLock = new object();

		public CSharpExtension Extension;

		public CSharpPluginLoader Loader;

		public string Name;

		public string Directory;

		public string ScriptName;

		public string ScriptPath;

		public string[] ScriptLines;

		public Encoding ScriptEncoding;

		public HashSet<string> Requires = new HashSet<string>();

		public HashSet<string> References = new HashSet<string>();

		public HashSet<string> IncludePaths = new HashSet<string>();

		public string CompilerErrors;

		public CompiledAssembly CompiledAssembly;

		public DateTime LastModifiedAt;

		public DateTime LastCachedScriptAt;

		public DateTime LastCompiledAt;

		public bool IsCompilationNeeded;

		protected Action<CSharpPlugin> LoadCallback;

		protected Action<bool> CompileCallback;

		protected float CompilationQueuedAt;

		private Oxide.Core.Libraries.Timer.TimerInstance timeoutTimer;

		public byte[] ScriptSource => ScriptEncoding.GetBytes(string.Join(Environment.NewLine, ScriptLines));

		public CompilableFile(CSharpExtension extension, CSharpPluginLoader loader, string directory, string name)
		{
			Extension = extension;
			Loader = loader;
			Directory = directory;
			ScriptName = name;
			ScriptPath = Path.Combine(Directory, ScriptName + ".cs");
			Name = Regex.Replace(ScriptName, "_", "");
			CheckLastModificationTime();
		}

		internal void Compile(Action<bool> callback)
		{
			lock (compileLock)
			{
				if (CompilationQueuedAt > 0f)
				{
					float num = Interface.Oxide.Now - CompilationQueuedAt;
					Interface.Oxide.LogDebug($"Plugin compilation is already queued: {ScriptName} ({num:0.000} ago)");
				}
				else
				{
					OnLoadingStarted();
					if (CompiledAssembly != null && !HasBeenModified() && (CompiledAssembly.IsLoading || !CompiledAssembly.IsBatch || CompiledAssembly.CompilablePlugins.All((CompilablePlugin pl) => pl.IsLoading)))
					{
						callback(obj: true);
					}
					else
					{
						IsCompilationNeeded = true;
						CompileCallback = callback;
						CompilationQueuedAt = Interface.Oxide.Now;
						OnCompilationRequested();
					}
				}
			}
		}

		internal virtual void OnCompilationStarted()
		{
			LastCompiledAt = LastModifiedAt;
			timeoutTimer?.Destroy();
			timeoutTimer = null;
			Interface.Oxide.NextTick(delegate
			{
				timeoutTimer?.Destroy();
				timeoutTimer = timer.Once(60f, OnCompilationTimeout);
			});
		}

		internal void OnCompilationSucceeded(CompiledAssembly compiledAssembly)
		{
			if (timeoutTimer == null)
			{
				Interface.Oxide.LogWarning("Ignored unexpected plugin compilation: " + Name);
				return;
			}
			timeoutTimer?.Destroy();
			timeoutTimer = null;
			IsCompilationNeeded = false;
			CompilationQueuedAt = 0f;
			CompiledAssembly = compiledAssembly;
			CompileCallback?.Invoke(obj: true);
		}

		internal void OnCompilationFailed()
		{
			if (timeoutTimer == null)
			{
				Interface.Oxide.LogWarning("Ignored unexpected plugin compilation failure: " + Name);
				return;
			}
			timeoutTimer?.Destroy();
			timeoutTimer = null;
			CompilationQueuedAt = 0f;
			LastCompiledAt = default(DateTime);
			CompileCallback?.Invoke(obj: false);
			IsCompilationNeeded = false;
		}

		internal void OnCompilationTimeout()
		{
			Interface.Oxide.LogError("Timed out waiting for plugin to be compiled: " + Name);
			CompilerErrors = "Timed out waiting for compilation";
			OnCompilationFailed();
		}

		internal bool HasBeenModified()
		{
			DateTime lastModifiedAt = LastModifiedAt;
			CheckLastModificationTime();
			return LastModifiedAt != lastModifiedAt;
		}

		internal void CheckLastModificationTime()
		{
			if (!File.Exists(ScriptPath))
			{
				LastModifiedAt = default(DateTime);
				return;
			}
			DateTime lastModificationTime = GetLastModificationTime();
			if (lastModificationTime != default(DateTime))
			{
				LastModifiedAt = lastModificationTime;
			}
		}

		internal DateTime GetLastModificationTime()
		{
			try
			{
				return File.GetLastWriteTime(ScriptPath);
			}
			catch (IOException ex)
			{
				Interface.Oxide.LogError("IOException while checking plugin: {0} ({1})", ScriptName, ex.Message);
				return default(DateTime);
			}
		}

		protected virtual void OnLoadingStarted()
		{
		}

		protected virtual void OnCompilationRequested()
		{
		}

		protected virtual void InitFailed(string message = null)
		{
			if (message != null)
			{
				Interface.Oxide.LogError(message);
			}
			LoadCallback?.Invoke(null);
		}
	}
}
