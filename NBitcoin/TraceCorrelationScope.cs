﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NBitcoin
{
	public class TraceCorrelationScope : IDisposable
	{
		private Guid activity;
		private Guid old;

		public Guid OldActivity
		{
			get
			{
				return old;
			}
			private set
			{
				old = value;
			}
		}

		bool _Transfered;

		TraceSource _Source;
		public TraceCorrelationScope(Guid activity, TraceSource source, bool traceTransfer)
		{
			this.old = Trace.CorrelationManager.ActivityId;
			this.activity = activity;

			_Transfered = old != activity && traceTransfer;
			if(_Transfered)
			{
				_Source = source;
				_Source.TraceTransfer(0, "transfer", activity);
			}
			Trace.CorrelationManager.ActivityId = activity;
		}


		#region IDisposable Members

		public void Dispose()
		{
			if(_Transfered)
			{
				_Source.TraceTransfer(0, "transfer", old);
			}
			Trace.CorrelationManager.ActivityId = old;
		}

		#endregion
	}
	public class TraceCorrelation
	{

		TraceSource _Source;
		string _ActivityName;
		public TraceCorrelation(TraceSource source, string activityName)
			: this(Guid.NewGuid(), source, activityName)
		{

		}
		public TraceCorrelation(Guid activity, TraceSource source, string activityName)
		{
			_Source = source;
			_ActivityName = activityName;
			this.activity = activity;
		}

		Guid activity;
		public Guid Activity
		{
			get
			{
				return activity;
			}
			private set
			{
				activity = value;
			}
		}

		volatile bool _First = true;
		public TraceCorrelationScope Open(bool traceTransfer = true)
		{
			var scope = new TraceCorrelationScope(activity, _Source, traceTransfer);
			if(_First)
			{
				_First = false;
				_Source.TraceEvent(TraceEventType.Start, 0, _ActivityName);
			}
			return scope;
		}

		public void LogInside(Action act)
		{
			using(Open())
			{
				act();
			}
		}








		public override string ToString()
		{
			return _ActivityName;
		}
	}
}
