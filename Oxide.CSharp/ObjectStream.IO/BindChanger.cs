using System;
using System.Reflection;
using System.Runtime.Serialization;

namespace ObjectStream.IO
{
	public class BindChanger : SerializationBinder
	{
		public override Type BindToType(string assemblyName, string typeName)
		{
			return Type.GetType($"{typeName}, {Assembly.GetExecutingAssembly().FullName}");
		}
	}
}
