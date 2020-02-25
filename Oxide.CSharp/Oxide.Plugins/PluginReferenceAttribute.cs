using System;

namespace Oxide.Plugins
{
	[AttributeUsage(AttributeTargets.Field)]
	public class PluginReferenceAttribute : Attribute
	{
		public string Name
		{
			get;
		}

		public PluginReferenceAttribute()
		{
		}

		public PluginReferenceAttribute(string name)
		{
			Name = name;
		}
	}
}
