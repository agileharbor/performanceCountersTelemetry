﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Palantiri.Counters;
using Palantiri.SensorObservers;
using Serilog;

namespace Palantiri.Sensors
{
	public class Sensor: ISensorObservable
	{
		protected PerforrmanceCounterProxy[] _counters{ get; set; }
		protected int _periodMs{ get; set; }
		protected bool _started{ get; set; }
		protected object _startLock = new object();
		protected CancellationTokenSource _sensorCts;
		protected Task _sensorTask;
		protected Task _sensorRefreshTask;
		protected CancellationToken _sensorCt;
		protected ConcurrentQueue< ConcurrentDictionary< string, float > > _countersQueue;
		protected ConcurrentDictionary< string, string > _countersAlias;
		protected List< ISensorObserver > _observers;
		private int _recreationPeriodMs;

		public Sensor( int periodMs, int recreationPeriodMs, params PerforrmanceCounterProxy[] counters )
		{
			_counters = counters;
			_periodMs = periodMs;
			_recreationPeriodMs = recreationPeriodMs;
			_countersQueue = new ConcurrentQueue< ConcurrentDictionary< string, float > >();
			_observers = new List< ISensorObserver >();
		}

		public void AddObservers( params ISensorObserver[] observers )
		{
			foreach( var sensorObserver in observers )
			{
				this._observers.Add( sensorObserver );
			}
		}

		public void RemoveObserver( ISensorObserver o )
		{
			this._observers.Remove( o );
		}

		public void NotifyObservers( ConcurrentDictionary< CounterAlias, CounterValue > counters )
		{
			foreach( var observer in this._observers )
			{
				observer.SendCounters( counters );
			}
		}

		public static string GetCounterId( PerformanceCounter x )
		{
			return x.CategoryName + "_" + x.CounterName + "_" + x.InstanceName;
		}

		public string GetCounterAlias( PerformanceCounter x )
		{
			string alias;
			if( this._countersAlias.TryGetValue( GetCounterId( x ), out alias ) )
				return alias;
			return null;
		}

		public ConcurrentDictionary< CounterAlias, CounterValue > GetCounterValues()
		{
			Log.Information( "Getting counters values..." );
			var counters = new ConcurrentDictionary< CounterAlias, CounterValue >();
			var dateTime = DateTime.UtcNow;
			Parallel.ForEach( this._counters.Where( x => x.Counter != null ), x =>
			{
				try
				{
					var value = x.Counter.NextValue();
					var nextValue = x.DevideByCpuCoresCount
						? value / PerformanceCounterHelper.CpuCoresCount
						: value;

					counters.AddOrUpdate( x.Alias, new CounterValue( dateTime, nextValue ), ( cid, y ) => new CounterValue( dateTime, nextValue ) );
					Log.Information( "Counter value received: [{alias}][{timepoint}][{value}].", x.Alias, dateTime, nextValue );
				}
				catch( Exception ex )
				{
					Log.Error( ex, "Can't get counter: " + x.ToString() );
				}
			} );

			Log.Information( "Counters values received." );
			return counters;
		}

		public void Stop()
		{
			Log.Information( "Stopping sensor..." );
			lock( this._startLock )
			{
				this._sensorCts.Cancel();
				this._started = false;
			}
			Log.Information( "Sensor stopped." );
		}

		public void Start()
		{
			Log.Information( "Starting sensor..." );
			lock( this._startLock )
			{
				if( this._started )
					return;

				this._started = true;
				this._sensorCts = new CancellationTokenSource();
				this._sensorCt = this._sensorCts.Token;
				this._sensorTask = Task.Factory.StartNew( () =>
				{
					while ( this._started && !this._sensorCt.IsCancellationRequested )
					{
						var countersValues = this.GetCounterValues();

						this.NotifyObservers( countersValues );
						Log.Debug( "Sensor observers notified" );
						try
						{
							Task.Delay( this._periodMs ).Wait( this._sensorCt );
						}
						catch ( OperationCanceledException e )
						{
							Log.Debug( "Cancellation requested" );
						}
					}
				} );
				this._sensorRefreshTask = Task.Factory.StartNew(() =>
				{
					while ( this._started && !this._sensorCt.IsCancellationRequested )
					{
						try
						{
							Task.Delay( this._recreationPeriodMs ).Wait(this._sensorCt);
						}
						catch (OperationCanceledException e)
						{
							Log.Debug("Cancellation requested");
							break;
						}
						Log.Information( "Sensor counters recreation started" );
						Parallel.ForEach( this._counters, x => x.ReFresh() );
						Log.Information( "Sensor counters recreation finished" );
					}
				}
				);

				Log.Information( "Sensor started." );
			}
		}

		public void RemoveCounters( PerforrmanceCounterProxy[] counters, Action<CounterAlias> onRemoved )
		{
			Log.Information( "Removing counters..." );
			lock( this._startLock )
			{
				var temp = this._counters.ToList();
				foreach( var counter in counters )
				{
					temp.Remove( counter );
					onRemoved( counter.Alias );
					Log.Information( "Counter marked for remove: {@counter}.", counter );
				}
				var tempArray = temp.ToArray();
				this._counters = tempArray;
				Log.Information( "Counters removed {@counters}.", counters );
			}
		}

		public void AddCounters( PerforrmanceCounterProxy[] counters )
		{
			Log.Information( "Adding counters..." );
			lock( this._startLock )
			{
				var temp = this._counters.ToList();
				temp.AddRange( counters );
				var tempArray = temp.ToArray();
				this._counters = tempArray;
				Log.Information( "Counters added: {@counters}.", counters );
			}
		}
	}
}