using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Oxide.Core;
using Oxide.Core.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Oxide.Plugins
{
	public class CompiledAssembly
	{
		public CompilablePlugin[] CompilablePlugins;

		public string[] PluginNames;

		public string Name;

		public DateTime CompiledAt;

		public byte[] RawAssembly;

		public byte[] PatchedAssembly;

		public float Duration;

		public Assembly LoadedAssembly;

		public bool IsLoading;

		private List<Action<bool>> loadCallbacks = new List<Action<bool>>();

		private bool isPatching;

		private bool isLoaded;

		public bool IsBatch => CompilablePlugins.Length > 1;

		private static IEnumerable<string> BlacklistedNamespaces => new string[14]
		{
			"Oxide.Core.ServerConsole",
			"System.IO",
			"System.Net",
			"System.Xml",
			"System.Reflection.Assembly",
			"System.Reflection.Emit",
			"System.Threading",
			"System.Runtime.InteropServices",
			"System.Diagnostics",
			"System.Security",
			"System.Timers",
			"Mono.CSharp",
			"Mono.Cecil",
			"ServerFileSystem"
		};

		private static IEnumerable<string> WhitelistedNamespaces => new string[13]
		{
			"System.Diagnostics.Stopwatch",
			"System.IO.MemoryStream",
			"System.IO.Stream",
			"System.IO.BinaryReader",
			"System.IO.BinaryWriter",
			"System.Net.Dns",
			"System.Net.Dns.GetHostEntry",
			"System.Net.IPAddress",
			"System.Net.IPEndPoint",
			"System.Net.NetworkInformation",
			"System.Net.Sockets.SocketFlags",
			"System.Security.Cryptography",
			"System.Threading.Interlocked"
		};

		public CompiledAssembly(string name, CompilablePlugin[] plugins, byte[] rawAssembly, float duration)
		{
			Name = name;
			CompilablePlugins = plugins;
			RawAssembly = rawAssembly;
			Duration = duration;
			PluginNames = CompilablePlugins.Select((CompilablePlugin pl) => pl.Name).ToArray();
		}

		public void LoadAssembly(Action<bool> callback)
		{
			if (isLoaded)
			{
				callback(obj: true);
				return;
			}
			IsLoading = true;
			loadCallbacks.Add(callback);
			if (!isPatching)
			{
				PatchAssembly(delegate(byte[] rawAssembly)
				{
					if (rawAssembly == null)
					{
						foreach (Action<bool> loadCallback in loadCallbacks)
						{
							loadCallback(obj: true);
						}
						loadCallbacks.Clear();
						IsLoading = false;
					}
					else
					{
						LoadedAssembly = Assembly.Load(rawAssembly);
						isLoaded = true;
						foreach (Action<bool> loadCallback2 in loadCallbacks)
						{
							loadCallback2(obj: true);
						}
						loadCallbacks.Clear();
						IsLoading = false;
					}
				});
			}
		}

		private void PatchAssembly(Action<byte[]> callback)
		{
			if (isPatching)
			{
				Interface.Oxide.LogWarning("Already patching plugin assembly: {0} (ignoring)", PluginNames.ToSentence());
				return;
			}
			_ = Interface.Oxide.Now;
			isPatching = true;
			MethodReference securityException = default(MethodReference);
			Action<TypeDefinition> patchModuleType = default(Action<TypeDefinition>);
			Exception ex = default(Exception);
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					AssemblyDefinition assemblyDefinition;
					using (MemoryStream stream = new MemoryStream(RawAssembly))
					{
						assemblyDefinition = AssemblyDefinition.ReadAssembly(stream);
					}
					ConstructorInfo constructor = typeof(UnauthorizedAccessException).GetConstructor(new Type[1]
					{
						typeof(string)
					});
					securityException = assemblyDefinition.MainModule.Import(constructor);
					patchModuleType = null;
					patchModuleType = delegate(TypeDefinition type)
					{
						foreach (MethodDefinition method in type.Methods)
						{
							bool flag = false;
							if (method.Body == null)
							{
								if (method.HasPInvokeInfo)
								{
									method.Attributes &= ~Mono.Cecil.MethodAttributes.PInvokeImpl;
									method.Body = new Mono.Cecil.Cil.MethodBody(method)
									{
										Instructions = 
										{
											Instruction.Create(OpCodes.Ldstr, "PInvoke access is restricted, you are not allowed to use PInvoke"),
											Instruction.Create(OpCodes.Newobj, securityException),
											Instruction.Create(OpCodes.Throw)
										}
									};
								}
							}
							else
							{
								bool flag2 = false;
								foreach (VariableDefinition variable in method.Body.Variables)
								{
									if (IsNamespaceBlacklisted(variable.VariableType.FullName))
									{
										method.Body = new Mono.Cecil.Cil.MethodBody(method)
										{
											Instructions = 
											{
												Instruction.Create(OpCodes.Ldstr, "System access is restricted, you are not allowed to use " + variable.VariableType.FullName),
												Instruction.Create(OpCodes.Newobj, securityException),
												Instruction.Create(OpCodes.Throw)
											}
										};
										flag2 = true;
										break;
									}
								}
								if (flag2)
								{
									continue;
								}
								Collection<Instruction> instructions = method.Body.Instructions;
								ILProcessor iLProcessor = method.Body.GetILProcessor();
								Instruction target = instructions.First();
								for (int i = 0; i < instructions.Count; i++)
								{
									if (flag)
									{
										break;
									}
									Instruction instruction = instructions[i];
									if (instruction.OpCode == OpCodes.Ldtoken)
									{
										string text = (instruction.Operand as IMetadataTokenProvider)?.ToString();
										if (IsNamespaceBlacklisted(text))
										{
											iLProcessor.InsertBefore(target, Instruction.Create(OpCodes.Ldstr, "System access is restricted, you are not allowed to use " + text));
											iLProcessor.InsertBefore(target, Instruction.Create(OpCodes.Newobj, securityException));
											iLProcessor.InsertBefore(target, Instruction.Create(OpCodes.Throw));
											flag = true;
										}
									}
									else if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Calli || instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Ldftn)
									{
										MethodReference methodReference = instruction.Operand as MethodReference;
										string text2 = methodReference?.DeclaringType.FullName;
										if ((text2 == "System.Type" && methodReference.Name == "GetType") || IsNamespaceBlacklisted(text2))
										{
											iLProcessor.InsertBefore(target, Instruction.Create(OpCodes.Ldstr, "System access is restricted, you are not allowed to use " + text2));
											iLProcessor.InsertBefore(target, Instruction.Create(OpCodes.Newobj, securityException));
											iLProcessor.InsertBefore(target, Instruction.Create(OpCodes.Throw));
											flag = true;
										}
									}
									else if (instruction.OpCode == OpCodes.Ldfld)
									{
										string text3 = (instruction.Operand as FieldReference)?.FieldType.FullName;
										if (IsNamespaceBlacklisted(text3))
										{
											iLProcessor.InsertBefore(target, Instruction.Create(OpCodes.Ldstr, "System access is restricted, you are not allowed to use " + text3));
											iLProcessor.InsertBefore(target, Instruction.Create(OpCodes.Newobj, securityException));
											iLProcessor.InsertBefore(target, Instruction.Create(OpCodes.Throw));
											flag = true;
										}
									}
								}
							}
							if (flag)
							{
								method.Body?.OptimizeMacros();
							}
						}
						foreach (TypeDefinition nestedType in type.NestedTypes)
						{
							patchModuleType(nestedType);
						}
					};
					foreach (TypeDefinition type2 in assemblyDefinition.MainModule.Types)
					{
						patchModuleType(type2);
						if (!IsCompilerGenerated(type2))
						{
							if (type2.Namespace == "Oxide.Plugins")
							{
								if (PluginNames.Contains(type2.Name))
								{
									if (type2.Methods.FirstOrDefault((MethodDefinition m) => !m.IsStatic && m.IsConstructor && !m.HasParameters && !m.IsPublic) != null)
									{
										CompilablePlugin compilablePlugin = CompilablePlugins.SingleOrDefault((CompilablePlugin p) => p.Name == type2.Name);
										if (compilablePlugin != null)
										{
											compilablePlugin.CompilerErrors = "Primary constructor in main class must be public";
										}
									}
									else
									{
										new DirectCallMethod(assemblyDefinition.MainModule, type2);
									}
								}
								else
								{
									Interface.Oxide.LogWarning((PluginNames.Length == 1) ? (PluginNames[0] + " has polluted the global namespace by defining " + type2.Name) : ("A plugin has polluted the global namespace by defining " + type2.Name));
								}
							}
							else if (type2.FullName != "<Module>" && !PluginNames.Any((string plugin) => type2.FullName.StartsWith("Oxide.Plugins." + plugin)))
							{
								Interface.Oxide.LogWarning((PluginNames.Length == 1) ? (PluginNames[0] + " has polluted the global namespace by defining " + type2.FullName) : ("A plugin has polluted the global namespace by defining " + type2.FullName));
							}
						}
					}
					foreach (TypeDefinition type3 in assemblyDefinition.MainModule.Types)
					{
						if (!(type3.Namespace != "Oxide.Plugins") && PluginNames.Contains(type3.Name))
						{
							foreach (MethodDefinition item in type3.Methods.Where((MethodDefinition m) => !m.IsStatic && !m.HasGenericParameters && !m.ReturnType.IsGenericParameter && !m.IsSetter && !m.IsGetter))
							{
								foreach (ParameterDefinition parameter in item.Parameters)
								{
									foreach (CustomAttribute customAttribute in parameter.CustomAttributes)
									{
										_ = customAttribute;
									}
								}
							}
						}
					}
					using (MemoryStream memoryStream = new MemoryStream())
					{
						assemblyDefinition.Write(memoryStream);
						PatchedAssembly = memoryStream.ToArray();
					}
					Interface.Oxide.NextTick(delegate
					{
						isPatching = false;
						callback(PatchedAssembly);
					});
				}
				catch (Exception ex2)
				{
					ex = ex2;
					Interface.Oxide.NextTick(delegate
					{
						isPatching = false;
						Interface.Oxide.LogException("Exception while patching: " + PluginNames.ToSentence(), ex);
						callback(null);
					});
				}
			});
		}

		public bool IsOutdated()
		{
			return CompilablePlugins.Any((CompilablePlugin pl) => pl.GetLastModificationTime() != CompiledAt);
		}

		private bool IsCompilerGenerated(TypeDefinition type)
		{
			return type.CustomAttributes.Any((CustomAttribute attr) => attr.Constructor.DeclaringType.ToString().Contains("CompilerGeneratedAttribute"));
		}

		private static bool IsNamespaceBlacklisted(string fullNamespace)
		{
			foreach (string blacklistedNamespace in BlacklistedNamespaces)
			{
				if (fullNamespace.StartsWith(blacklistedNamespace) && !WhitelistedNamespaces.Any(fullNamespace.StartsWith))
				{
					return true;
				}
			}
			return false;
		}
	}
}
