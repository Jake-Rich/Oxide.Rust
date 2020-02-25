using System;
using System.Threading;

namespace ObjectStream.Threading
{
	internal class Worker
	{
		public event WorkerExceptionEventHandler Error;

		public void DoWork(Action action)
		{
			Thread thread = new Thread(DoWorkImpl);
			thread.IsBackground = true;
			thread.Start(action);
		}

		private void DoWorkImpl(object oAction)
		{
			Action action = (Action)oAction;
			try
			{
				action();
			}
			catch (Exception ex)
			{
				Exception e = ex;
				Callback(delegate
				{
					Fail(e);
				});
			}
		}

		private void Fail(Exception exception)
		{
			if (this.Error != null)
			{
				this.Error(exception);
			}
		}

		private void Callback(Action action)
		{
			Thread thread = new Thread(action.Invoke);
			thread.IsBackground = true;
			thread.Start();
		}
	}
}
