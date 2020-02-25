using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System.Reflection;

namespace Oxide.Plugins
{
	public class CovalencePlugin : CSharpPlugin
	{
		private new static readonly Covalence covalence = Interface.Oxide.GetLibrary<Covalence>();

		protected string game = covalence.Game;

		protected IPlayerManager players = covalence.Players;

		protected IServer server = covalence.Server;

		protected void Log(string format, params object[] args)
		{
			Interface.Oxide.LogInfo("[{0}] {1}", base.Title, (args.Length != 0) ? string.Format(format, args) : format);
		}

		protected void LogWarning(string format, params object[] args)
		{
			Interface.Oxide.LogWarning("[{0}] {1}", base.Title, (args.Length != 0) ? string.Format(format, args) : format);
		}

		protected void LogError(string format, params object[] args)
		{
			Interface.Oxide.LogError("[{0}] {1}", base.Title, (args.Length != 0) ? string.Format(format, args) : format);
		}

		public override void HandleAddedToManager(PluginManager manager)
		{
			MethodInfo[] methods = GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
			foreach (MethodInfo method in methods)
			{
				object[] customAttributes = method.GetCustomAttributes(typeof(CommandAttribute), inherit: true);
				object[] customAttributes2 = method.GetCustomAttributes(typeof(PermissionAttribute), inherit: true);
				if (customAttributes.Length != 0)
				{
					CommandAttribute commandAttribute = customAttributes[0] as CommandAttribute;
					PermissionAttribute permissionAttribute = (customAttributes2.Length == 0) ? null : (customAttributes2[0] as PermissionAttribute);
					if (commandAttribute != null)
					{
						AddCovalenceCommand(commandAttribute.Commands, permissionAttribute?.Permission, delegate(IPlayer caller, string command, string[] args)
						{
							CallHook(method.Name, caller, command, args);
							return true;
						});
					}
				}
			}
			base.HandleAddedToManager(manager);
		}
	}
}
