using System;

namespace Oxide.Plugins
{
	[AttributeUsage(AttributeTargets.Method)]
	public class PermissionAttribute : Attribute
	{
		public string[] Permission
		{
			get;
		}

		public PermissionAttribute(string permission)
		{
			Permission = new string[1]
			{
				permission
			};
		}
	}
}
