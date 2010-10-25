﻿using System;

using Deveel.Data.Net.Client;

namespace Deveel.Data.Net {
	public interface IService : IDisposable {
		ServiceType ServiceType { get; }

		ServiceState State { get; }
		
		IMessageProcessor Processor { get; }
		
		void Start();

		void Stop();
	}
}