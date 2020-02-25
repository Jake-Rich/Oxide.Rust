using System;

namespace Oxide.Plugins
{
	[AttributeUsage(AttributeTargets.Method)]
	public class ChatCommandAttribute : Attribute
	{
		public string Command
		{
			get;
			private set;
		}

		public ChatCommandAttribute(string command)
		{
			Command = command;
		}
	}
}
