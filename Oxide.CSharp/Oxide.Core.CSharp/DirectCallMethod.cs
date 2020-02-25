using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Oxide.Core.CSharp
{
	public class DirectCallMethod
	{
		public class Node
		{
			public char Char;

			public string Name;

			public Dictionary<char, Node> Edges = new Dictionary<char, Node>();

			public Node Parent;

			public Instruction FirstInstruction;
		}

		private ModuleDefinition module;

		private TypeDefinition type;

		private MethodDefinition method;

		private MethodBody body;

		private Instruction endInstruction;

		private Dictionary<Instruction, Node> jumpToEdgePlaceholderTargets = new Dictionary<Instruction, Node>();

		private List<Instruction> jumpToEndPlaceholders = new List<Instruction>();

		private Dictionary<string, MethodDefinition> hookMethods = new Dictionary<string, MethodDefinition>();

		private MethodReference getLength;

		private MethodReference getChars;

		private MethodReference isNullOrEmpty;

		private MethodReference stringEquals;

		private string hook_attribute = typeof(HookMethodAttribute).FullName;

		public DirectCallMethod(ModuleDefinition module, TypeDefinition type)
		{
			this.module = module;
			this.type = type;
			getLength = module.Import(typeof(string).GetMethod("get_Length", new Type[0]));
			getChars = module.Import(typeof(string).GetMethod("get_Chars", new Type[1]
			{
				typeof(int)
			}));
			isNullOrEmpty = module.Import(typeof(string).GetMethod("IsNullOrEmpty", new Type[1]
			{
				typeof(string)
			}));
			stringEquals = module.Import(typeof(string).GetMethod("Equals", new Type[1]
			{
				typeof(string)
			}));
			AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(Path.Combine(Interface.Oxide.ExtensionDirectory, "Oxide.CSharp.dll"));
			ModuleDefinition mainModule = assemblyDefinition.MainModule;
			TypeDefinition typeDefinition = module.Import(assemblyDefinition.MainModule.GetType("Oxide.Plugins.CSharpPlugin")).Resolve();
			MethodDefinition methodDefinition = module.Import(typeDefinition.Methods.First((MethodDefinition method) => method.Name == "DirectCallHook")).Resolve();
			method = new MethodDefinition(methodDefinition.Name, methodDefinition.Attributes, mainModule.Import(methodDefinition.ReturnType))
			{
				DeclaringType = type
			};
			foreach (ParameterDefinition parameter in methodDefinition.Parameters)
			{
				ParameterDefinition parameterDefinition = new ParameterDefinition(parameter.Name, parameter.Attributes, mainModule.Import(parameter.ParameterType))
				{
					IsOut = parameter.IsOut,
					Constant = parameter.Constant,
					MarshalInfo = parameter.MarshalInfo,
					IsReturnValue = parameter.IsReturnValue
				};
				foreach (CustomAttribute customAttribute in parameter.CustomAttributes)
				{
					parameterDefinition.CustomAttributes.Add(new CustomAttribute(module.Import(customAttribute.Constructor)));
				}
				method.Parameters.Add(parameterDefinition);
			}
			foreach (CustomAttribute customAttribute2 in methodDefinition.CustomAttributes)
			{
				method.CustomAttributes.Add(new CustomAttribute(module.Import(customAttribute2.Constructor)));
			}
			method.ImplAttributes = methodDefinition.ImplAttributes;
			method.SemanticsAttributes = methodDefinition.SemanticsAttributes;
			method.Attributes &= ~MethodAttributes.VtableLayoutMask;
			method.Attributes |= MethodAttributes.CompilerControlled;
			body = new MethodBody(method);
			body.SimplifyMacros();
			method.Body = body;
			type.Methods.Add(method);
			body.Variables.Add(new VariableDefinition("name_size", module.TypeSystem.Int32));
			body.Variables.Add(new VariableDefinition("i", module.TypeSystem.Int32));
			AddInstruction(OpCodes.Ldarg_2);
			AddInstruction(OpCodes.Ldnull);
			AddInstruction(OpCodes.Stind_Ref);
			AddInstruction(OpCodes.Ldarg_1);
			AddInstruction(OpCodes.Call, isNullOrEmpty);
			Instruction instruction = AddInstruction(OpCodes.Brfalse, body.Instructions[0]);
			Return(value: false);
			instruction.Operand = AddInstruction(OpCodes.Ldarg_1);
			AddInstruction(OpCodes.Callvirt, getLength);
			AddInstruction(OpCodes.Stloc_0);
			AddInstruction(OpCodes.Ldc_I4_0);
			AddInstruction(OpCodes.Stloc_1);
			foreach (MethodDefinition item in type.Methods.Where((MethodDefinition m) => !m.IsStatic && (m.IsPrivate || IsHookMethod(m)) && !m.HasGenericParameters && !m.ReturnType.IsGenericParameter && m.DeclaringType == type && !m.IsSetter && !m.IsGetter))
			{
				if (!item.Name.Contains("<"))
				{
					string text = item.Name;
					if (item.Parameters.Count > 0)
					{
						text = text + "(" + string.Join(", ", item.Parameters.Select((ParameterDefinition x) => x.ParameterType.ToString().Replace("/", "+").Replace("<", "[")
							.Replace(">", "]")).ToArray()) + ")";
					}
					if (!hookMethods.ContainsKey(text))
					{
						hookMethods[text] = item;
					}
				}
			}
			Node node = new Node();
			foreach (string key in hookMethods.Keys)
			{
				Node node2 = node;
				for (int i = 1; i <= key.Length; i++)
				{
					char c = key[i - 1];
					if (!node2.Edges.TryGetValue(c, out Node value))
					{
						value = new Node
						{
							Parent = node2,
							Char = c
						};
						node2.Edges[c] = value;
					}
					if (i == key.Length)
					{
						value.Name = key;
					}
					node2 = value;
				}
			}
			int num = 1;
			foreach (char key2 in node.Edges.Keys)
			{
				BuildNode(node.Edges[key2], num++);
			}
			endInstruction = Return(value: false);
			foreach (Instruction key3 in jumpToEdgePlaceholderTargets.Keys)
			{
				key3.Operand = jumpToEdgePlaceholderTargets[key3].FirstInstruction;
			}
			foreach (Instruction jumpToEndPlaceholder in jumpToEndPlaceholders)
			{
				jumpToEndPlaceholder.Operand = endInstruction;
			}
			body.OptimizeMacros();
		}

		private bool IsHookMethod(MethodDefinition method)
		{
			foreach (CustomAttribute customAttribute in method.CustomAttributes)
			{
				if (customAttribute.AttributeType.FullName == hook_attribute)
				{
					return true;
				}
			}
			return false;
		}

		private void BuildNode(Node node, int edge_number)
		{
			if (edge_number == 1)
			{
				node.FirstInstruction = AddInstruction(OpCodes.Ldloc_1);
				AddInstruction(OpCodes.Ldloc_0);
				jumpToEndPlaceholders.Add(AddInstruction(OpCodes.Bge, body.Instructions[0]));
			}
			if (edge_number == 1)
			{
				AddInstruction(OpCodes.Ldarg_1);
			}
			else
			{
				node.FirstInstruction = AddInstruction(OpCodes.Ldarg_1);
			}
			AddInstruction(OpCodes.Ldloc_1);
			AddInstruction(OpCodes.Callvirt, getChars);
			AddInstruction(Ldc_I4_n(node.Char));
			if (node.Parent.Edges.Count > edge_number)
			{
				JumpToEdge(node.Parent.Edges.Values.ElementAt(edge_number));
			}
			else
			{
				JumpToEnd();
			}
			if (node.Edges.Count == 1 && node.Name == null)
			{
				Node node2 = node;
				while (node2.Edges.Count == 1 && node2.Name == null)
				{
					node2 = node2.Edges.Values.First();
				}
				if (node2.Edges.Count == 0 && node2.Name != null)
				{
					AddInstruction(OpCodes.Ldarg_1);
					AddInstruction(Instruction.Create(OpCodes.Ldstr, node2.Name));
					AddInstruction(OpCodes.Callvirt, stringEquals);
					jumpToEndPlaceholders.Add(AddInstruction(OpCodes.Brfalse, body.Instructions[0]));
					CallMethod(hookMethods[node2.Name]);
					Return(value: true);
					return;
				}
			}
			AddInstruction(OpCodes.Ldloc_1);
			AddInstruction(OpCodes.Ldc_I4_1);
			AddInstruction(OpCodes.Add);
			AddInstruction(OpCodes.Stloc_1);
			if (node.Name != null)
			{
				AddInstruction(OpCodes.Ldloc_1);
				AddInstruction(OpCodes.Ldloc_0);
				if (node.Edges.Count > 0)
				{
					JumpToEdge(node.Edges.Values.First());
				}
				else
				{
					JumpToEnd();
				}
				CallMethod(hookMethods[node.Name]);
				Return(value: true);
			}
			int num = 1;
			foreach (char key in node.Edges.Keys)
			{
				BuildNode(node.Edges[key], num++);
			}
		}

		private void CallMethod(MethodDefinition method)
		{
			Dictionary<ParameterDefinition, VariableDefinition> dictionary = new Dictionary<ParameterDefinition, VariableDefinition>();
			for (int i = 0; i < method.Parameters.Count; i++)
			{
				ParameterDefinition parameterDefinition = method.Parameters[i];
				ByReferenceType byReferenceType = parameterDefinition.ParameterType as ByReferenceType;
				if (byReferenceType != null)
				{
					VariableDefinition value = AddVariable(module.Import(byReferenceType.ElementType));
					AddInstruction(OpCodes.Ldarg_3);
					AddInstruction(Ldc_I4_n(i));
					AddInstruction(OpCodes.Ldelem_Ref);
					AddInstruction(OpCodes.Unbox_Any, module.Import(byReferenceType.ElementType));
					AddInstruction(OpCodes.Stloc_S, value);
					dictionary[parameterDefinition] = value;
				}
			}
			if (method.ReturnType.Name != "Void")
			{
				AddInstruction(OpCodes.Ldarg_2);
			}
			AddInstruction(OpCodes.Ldarg_0);
			for (int j = 0; j < method.Parameters.Count; j++)
			{
				ParameterDefinition parameterDefinition2 = method.Parameters[j];
				if (parameterDefinition2.ParameterType is ByReferenceType)
				{
					AddInstruction(OpCodes.Ldloca, dictionary[parameterDefinition2]);
					continue;
				}
				AddInstruction(OpCodes.Ldarg_3);
				AddInstruction(Ldc_I4_n(j));
				AddInstruction(OpCodes.Ldelem_Ref);
				AddInstruction(OpCodes.Unbox_Any, module.Import(parameterDefinition2.ParameterType));
			}
			AddInstruction(OpCodes.Call, module.Import(method));
			for (int k = 0; k < method.Parameters.Count; k++)
			{
				ParameterDefinition parameterDefinition3 = method.Parameters[k];
				ByReferenceType byReferenceType2 = parameterDefinition3.ParameterType as ByReferenceType;
				if (byReferenceType2 != null)
				{
					AddInstruction(OpCodes.Ldarg_3);
					AddInstruction(Ldc_I4_n(k));
					AddInstruction(OpCodes.Ldloc_S, dictionary[parameterDefinition3]);
					AddInstruction(OpCodes.Box, module.Import(byReferenceType2.ElementType));
					AddInstruction(OpCodes.Stelem_Ref);
				}
			}
			if (method.ReturnType.Name != "Void")
			{
				if (method.ReturnType.Name != "Object")
				{
					AddInstruction(OpCodes.Box, module.Import(method.ReturnType));
				}
				AddInstruction(OpCodes.Stind_Ref);
			}
		}

		private Instruction Return(bool value)
		{
			Instruction result = AddInstruction(Ldc_I4_n(value ? 1 : 0));
			AddInstruction(OpCodes.Ret);
			return result;
		}

		private void JumpToEdge(Node node)
		{
			Instruction key = AddInstruction(OpCodes.Bne_Un, body.Instructions[1]);
			jumpToEdgePlaceholderTargets[key] = node;
		}

		private void JumpToEnd()
		{
			jumpToEndPlaceholders.Add(AddInstruction(OpCodes.Bne_Un, body.Instructions[0]));
		}

		private Instruction AddInstruction(OpCode opcode)
		{
			return AddInstruction(Instruction.Create(opcode));
		}

		private Instruction AddInstruction(OpCode opcode, Instruction instruction)
		{
			return AddInstruction(Instruction.Create(opcode, instruction));
		}

		private Instruction AddInstruction(OpCode opcode, MethodReference method_reference)
		{
			return AddInstruction(Instruction.Create(opcode, method_reference));
		}

		private Instruction AddInstruction(OpCode opcode, TypeReference type_reference)
		{
			return AddInstruction(Instruction.Create(opcode, type_reference));
		}

		private Instruction AddInstruction(OpCode opcode, int value)
		{
			return AddInstruction(Instruction.Create(opcode, value));
		}

		private Instruction AddInstruction(OpCode opcode, VariableDefinition value)
		{
			return AddInstruction(Instruction.Create(opcode, value));
		}

		private Instruction AddInstruction(Instruction instruction)
		{
			body.Instructions.Add(instruction);
			return instruction;
		}

		public VariableDefinition AddVariable(TypeReference typeRef, string name = "")
		{
			VariableDefinition variableDefinition = new VariableDefinition(name, typeRef);
			body.Variables.Add(variableDefinition);
			return variableDefinition;
		}

		private Instruction Ldc_I4_n(int n)
		{
			switch (n)
			{
			case 0:
				return Instruction.Create(OpCodes.Ldc_I4_0);
			case 1:
				return Instruction.Create(OpCodes.Ldc_I4_1);
			case 2:
				return Instruction.Create(OpCodes.Ldc_I4_2);
			case 3:
				return Instruction.Create(OpCodes.Ldc_I4_3);
			case 4:
				return Instruction.Create(OpCodes.Ldc_I4_4);
			case 5:
				return Instruction.Create(OpCodes.Ldc_I4_5);
			case 6:
				return Instruction.Create(OpCodes.Ldc_I4_6);
			case 7:
				return Instruction.Create(OpCodes.Ldc_I4_7);
			case 8:
				return Instruction.Create(OpCodes.Ldc_I4_8);
			default:
				return Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)n);
			}
		}
	}
}
