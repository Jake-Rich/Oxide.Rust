using System;
using System.Linq;

namespace Oxide.Plugins
{
	[AttributeUsage(AttributeTargets.Method)]
	public class ConsoleCommandAttribute : Attribute
	{
		public string Command
		{
			get;
			private set;
		}

		public ConsoleCommandAttribute(string command)
		{
			Command = (command.Contains('.') ? command : ("global." + command));
		}
	}
}
