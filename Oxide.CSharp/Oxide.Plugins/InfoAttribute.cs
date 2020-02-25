using Oxide.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
	[AttributeUsage(AttributeTargets.Class)]
	public class InfoAttribute : Attribute
	{
		public string Title
		{
			get;
		}

		public string Author
		{
			get;
		}

		public VersionNumber Version
		{
			get;
			private set;
		}

		public int ResourceId
		{
			get;
			set;
		}

		public InfoAttribute(string Title, string Author, string Version)
		{
			this.Title = Title;
			this.Author = Author;
			SetVersion(Version);
		}

		public InfoAttribute(string Title, string Author, double Version)
		{
			this.Title = Title;
			this.Author = Author;
			SetVersion(Version.ToString());
		}

		private void SetVersion(string version)
		{
			ushort result;
			List<ushort> list = (from part in version.Split('.')
				select (ushort)(ushort.TryParse(part, out result) ? result : 0)).ToList();
			while (list.Count < 3)
			{
				list.Add(0);
			}
			if (list.Count > 3)
			{
				Interface.Oxide.LogWarning("Version `" + version + "` is invalid for " + Title + ", should be `major.minor.patch`");
			}
			Version = new VersionNumber(list[0], list[1], list[2]);
		}
	}
}
