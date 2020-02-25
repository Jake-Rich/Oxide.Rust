using ObjectStream.IO;
using ObjectStream.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ObjectStream
{
	public class ObjectStreamConnection<TRead, TWrite> where TRead : class where TWrite : class
	{
		private readonly ObjectStreamWrapper<TRead, TWrite> _streamWrapper;

		private readonly Queue<TWrite> _writeQueue = new Queue<TWrite>();

		private readonly AutoResetEvent _writeSignal = new AutoResetEvent(initialState: false);

		public event ConnectionMessageEventHandler<TRead, TWrite> ReceiveMessage;

		public event ConnectionExceptionEventHandler<TRead, TWrite> Error;

		internal ObjectStreamConnection(Stream inStream, Stream outStream)
		{
			_streamWrapper = new ObjectStreamWrapper<TRead, TWrite>(inStream, outStream);
		}

		public void Open()
		{
			Worker worker = new Worker();
			worker.Error += OnError;
			worker.DoWork(ReadStream);
			Worker worker2 = new Worker();
			worker2.Error += OnError;
			worker2.DoWork(WriteStream);
		}

		public void PushMessage(TWrite message)
		{
			_writeQueue.Enqueue(message);
			_writeSignal.Set();
		}

		public void Close()
		{
			CloseImpl();
		}

		private void CloseImpl()
		{
			this.Error = null;
			_streamWrapper.Close();
			_writeSignal.Set();
		}

		private void OnError(Exception exception)
		{
			if (this.Error != null)
			{
				this.Error(this, exception);
			}
		}

		private void ReadStream()
		{
			TRead val;
			do
			{
				if (_streamWrapper.CanRead)
				{
					val = _streamWrapper.ReadObject();
					this.ReceiveMessage?.Invoke(this, val);
					continue;
				}
				return;
			}
			while (val != null);
			CloseImpl();
		}

		private void WriteStream()
		{
			while (_streamWrapper.CanWrite)
			{
				_writeSignal.WaitOne();
				while (_writeQueue.Count > 0)
				{
					_streamWrapper.WriteObject(_writeQueue.Dequeue());
				}
			}
		}
	}
}
