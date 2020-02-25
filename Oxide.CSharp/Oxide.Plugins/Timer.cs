using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using System;

namespace Oxide.Plugins
{
	public class Timer
	{
		private Oxide.Core.Libraries.Timer.TimerInstance instance;

		public int Repetitions => instance.Repetitions;

		public float Delay => instance.Delay;

		public Action Callback => instance.Callback;

		public bool Destroyed => instance.Destroyed;

		public Plugin Owner => instance.Owner;

		public Timer(Oxide.Core.Libraries.Timer.TimerInstance instance)
		{
			this.instance = instance;
		}

		public void Reset(float delay = -1f, int repetitions = 1)
		{
			instance.Reset(delay, repetitions);
		}

		public void Destroy()
		{
			instance.Destroy();
		}

		public void DestroyToPool()
		{
			instance.DestroyToPool();
		}
	}
}
