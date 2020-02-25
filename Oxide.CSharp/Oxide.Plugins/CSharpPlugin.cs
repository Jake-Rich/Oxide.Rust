using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Core.Plugins.Watchers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Oxide.Plugins
{
	public abstract class CSharpPlugin : CSPlugin
	{
		public class PluginFieldInfo
		{
			public Plugin Plugin;

			public FieldInfo Field;

			public Type FieldType;

			public Type[] GenericArguments;

			public Dictionary<string, MethodInfo> Methods = new Dictionary<string, MethodInfo>();

			public object Value => Field.GetValue(Plugin);

			public PluginFieldInfo(Plugin plugin, FieldInfo field)
			{
				Plugin = plugin;
				Field = field;
				FieldType = field.FieldType;
				GenericArguments = FieldType.GetGenericArguments();
			}

			public bool HasValidConstructor(params Type[] argument_types)
			{
				Type type = GenericArguments[1];
				if (!(type.GetConstructor(new Type[0]) != null))
				{
					return type.GetConstructor(argument_types) != null;
				}
				return true;
			}

			public bool LookupMethod(string method_name, params Type[] argument_types)
			{
				MethodInfo method = FieldType.GetMethod(method_name, argument_types);
				if (method == null)
				{
					return false;
				}
				Methods[method_name] = method;
				return true;
			}

			public object Call(string method_name, params object[] args)
			{
				if (!Methods.TryGetValue(method_name, out MethodInfo value))
				{
					value = FieldType.GetMethod(method_name, BindingFlags.Instance | BindingFlags.Public);
					Methods[method_name] = value;
				}
				if (value == null)
				{
					throw new MissingMethodException(FieldType.Name, method_name);
				}
				return value.Invoke(Value, args);
			}
		}

		public FSWatcher Watcher;

		protected Covalence covalence = Interface.Oxide.GetLibrary<Covalence>();

		protected Lang lang = Interface.Oxide.GetLibrary<Lang>();

		protected Oxide.Core.Libraries.Plugins plugins = Interface.Oxide.GetLibrary<Oxide.Core.Libraries.Plugins>();

		protected Permission permission = Interface.Oxide.GetLibrary<Permission>();

		protected WebRequests webrequest = Interface.Oxide.GetLibrary<WebRequests>();

		protected PluginTimers timer;

		protected HashSet<PluginFieldInfo> onlinePlayerFields = new HashSet<PluginFieldInfo>();

		private Dictionary<string, FieldInfo> pluginReferenceFields = new Dictionary<string, FieldInfo>();

		private bool hookDispatchFallback;

		public bool HookedOnFrame
		{
			get;
			private set;
		}

		public CSharpPlugin()
		{
			timer = new PluginTimers(this);
			Type type = GetType();
			FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				object[] customAttributes = fieldInfo.GetCustomAttributes(typeof(PluginReferenceAttribute), inherit: true);
				if (customAttributes.Length != 0)
				{
					PluginReferenceAttribute pluginReferenceAttribute = customAttributes[0] as PluginReferenceAttribute;
					pluginReferenceFields[pluginReferenceAttribute.Name ?? fieldInfo.Name] = fieldInfo;
				}
			}
			MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
			foreach (MethodInfo methodInfo in methods)
			{
				if (methodInfo.GetCustomAttributes(typeof(HookMethodAttribute), inherit: true).Length == 0)
				{
					if (methodInfo.Name.Equals("OnFrame"))
					{
						HookedOnFrame = true;
					}
					if (methodInfo.DeclaringType.Name == type.Name)
					{
						AddHookMethod(methodInfo.Name, methodInfo);
					}
				}
			}
		}

		public virtual bool SetPluginInfo(string name, string path)
		{
			base.Name = name;
			base.Filename = path;
			object[] customAttributes = GetType().GetCustomAttributes(typeof(InfoAttribute), inherit: true);
			if (customAttributes.Length != 0)
			{
				InfoAttribute infoAttribute = customAttributes[0] as InfoAttribute;
				base.Title = infoAttribute.Title;
				base.Author = infoAttribute.Author;
				base.Version = infoAttribute.Version;
				base.ResourceId = infoAttribute.ResourceId;
				object[] customAttributes2 = GetType().GetCustomAttributes(typeof(DescriptionAttribute), inherit: true);
				if (customAttributes2.Length != 0)
				{
					DescriptionAttribute descriptionAttribute = customAttributes2[0] as DescriptionAttribute;
					base.Description = descriptionAttribute.Description;
				}
				MethodInfo method = GetType().GetMethod("LoadDefaultConfig", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				base.HasConfig = (method.DeclaringType != typeof(Plugin));
				MethodInfo method2 = GetType().GetMethod("LoadDefaultMessages", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				base.HasMessages = (method2.DeclaringType != typeof(Plugin));
				return true;
			}
			Interface.Oxide.LogWarning("Failed to load " + name + ": Info attribute missing");
			return false;
		}

		public override void HandleAddedToManager(PluginManager manager)
		{
			base.HandleAddedToManager(manager);
			if (base.Filename != null)
			{
				Watcher.AddMapping(base.Name);
			}
			foreach (string key in pluginReferenceFields.Keys)
			{
				pluginReferenceFields[key].SetValue(this, manager.GetPlugin(key));
			}
			try
			{
				OnCallHook("Loaded", null);
			}
			catch (Exception ex)
			{
				Interface.Oxide.LogException($"Failed to initialize plugin '{base.Name} v{base.Version}'", ex);
				base.Loader.PluginErrors[base.Name] = ex.Message;
			}
		}

		public override void HandleRemovedFromManager(PluginManager manager)
		{
			if (base.IsLoaded)
			{
				CallHook("Unload", null);
			}
			Watcher.RemoveMapping(base.Name);
			foreach (string key in pluginReferenceFields.Keys)
			{
				pluginReferenceFields[key].SetValue(this, null);
			}
			base.HandleRemovedFromManager(manager);
		}

		public virtual bool DirectCallHook(string name, out object ret, object[] args)
		{
			ret = null;
			return false;
		}

		protected override object InvokeMethod(HookMethod method, object[] args)
		{
			if (!hookDispatchFallback && !method.IsBaseHook)
			{
				if (args != null && args.Length != 0)
				{
					ParameterInfo[] parameters = method.Parameters;
					for (int i = 0; i < args.Length; i++)
					{
						object obj = args[i];
						if (obj == null)
						{
							continue;
						}
						Type parameterType = parameters[i].ParameterType;
						if (parameterType.IsValueType)
						{
							Type type = obj.GetType();
							if (parameterType != typeof(object) && type != parameterType)
							{
								args[i] = Convert.ChangeType(obj, parameterType);
							}
						}
					}
				}
				try
				{
					if (DirectCallHook(method.Name, out object ret, args))
					{
						return ret;
					}
					PrintWarning("Unable to call hook directly: " + method.Name);
				}
				catch (InvalidProgramException ex)
				{
					Interface.Oxide.LogError("Hook dispatch failure detected, falling back to reflection based dispatch. " + ex);
					CompilablePlugin compilablePlugin = CSharpPluginLoader.GetCompilablePlugin(Interface.Oxide.PluginDirectory, base.Name);
					if (compilablePlugin?.CompiledAssembly != null)
					{
						File.WriteAllBytes(Interface.Oxide.PluginDirectory + "\\" + base.Name + ".dump", compilablePlugin.CompiledAssembly.PatchedAssembly);
						Interface.Oxide.LogWarning("The invalid raw assembly has been dumped to Plugins/" + base.Name + ".dump");
					}
					hookDispatchFallback = true;
				}
			}
			return method.Method.Invoke(this, args);
		}

		public void SetFailState(string reason)
		{
			throw new PluginLoadFailure(reason);
		}

		[HookMethod("OnPluginLoaded")]
		private void base_OnPluginLoaded(Plugin plugin)
		{
			if (pluginReferenceFields.TryGetValue(plugin.Name, out FieldInfo value))
			{
				value.SetValue(this, plugin);
			}
		}

		[HookMethod("OnPluginUnloaded")]
		private void base_OnPluginUnloaded(Plugin plugin)
		{
			if (pluginReferenceFields.TryGetValue(plugin.Name, out FieldInfo value))
			{
				value.SetValue(this, null);
			}
		}

		protected void Puts(string format, params object[] args)
		{
			Interface.Oxide.LogInfo("[{0}] {1}", base.Title, (args.Length != 0) ? string.Format(format, args) : format);
		}

		protected void PrintWarning(string format, params object[] args)
		{
			Interface.Oxide.LogWarning("[{0}] {1}", base.Title, (args.Length != 0) ? string.Format(format, args) : format);
		}

		protected void PrintError(string format, params object[] args)
		{
			Interface.Oxide.LogError("[{0}] {1}", base.Title, (args.Length != 0) ? string.Format(format, args) : format);
		}

		protected void LogToFile(string filename, string text, Plugin plugin, bool timeStamp = true)
		{
			string text2 = Path.Combine(Interface.Oxide.LogDirectory, plugin.Name);
			if (!Directory.Exists(text2))
			{
				Directory.CreateDirectory(text2);
			}
			filename = plugin.Name.ToLower() + "_" + filename.ToLower() + (timeStamp ? $"-{DateTime.Now:yyyy-MM-dd}" : "") + ".txt";
			using (StreamWriter streamWriter = new StreamWriter(Path.Combine(text2, Utility.CleanPath(filename)), append: true))
			{
				streamWriter.WriteLine(text);
			}
		}

		protected void NextFrame(Action callback)
		{
			Interface.Oxide.NextTick(callback);
		}

		protected void NextTick(Action callback)
		{
			Interface.Oxide.NextTick(callback);
		}

		protected void QueueWorkerThread(Action<object> callback)
		{
			ThreadPool.QueueUserWorkItem(delegate(object context)
			{
				try
				{
					callback(context);
				}
				catch (Exception ex)
				{
					RaiseError($"Exception in '{base.Name} v{base.Version}' plugin worker thread: {ex.ToString()}");
				}
			});
		}
	}
}
