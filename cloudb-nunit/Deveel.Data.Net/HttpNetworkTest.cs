﻿using System;

using Deveel.Data.Configuration;

using NUnit.Framework;

namespace Deveel.Data.Net {
	[TestFixture(HttpMessageFormat.Xml, NetworkStoreType.Memory)]
	[TestFixture(HttpMessageFormat.Xml, NetworkStoreType.FileSystem)]
	[TestFixture(HttpMessageFormat.Json, NetworkStoreType.Memory)]
	[TestFixture(HttpMessageFormat.Json, NetworkStoreType.FileSystem)]
	public sealed class HttpNetworkTest : NetworkTestBase {
		private readonly HttpMessageFormat format;
				
		private static readonly HttpServiceAddress Local = new HttpServiceAddress("localhost", 1587);

		
		public HttpNetworkTest(HttpMessageFormat format, NetworkStoreType storeType)
			: base(storeType) {
			this.format = format;
		}

		protected override IServiceAddress LocalAddress {
			get { return Local; }
		}
		
		protected override AdminService CreateAdminService(NetworkStoreType storeType) {
			IServiceFactory serviceFactory = null;
			if (storeType == NetworkStoreType.Memory) {
				serviceFactory = new MemoryServiceFactory();
			} else if (storeType == NetworkStoreType.FileSystem) {
				serviceFactory = new FileSystemServiceFactory(TestPath);
			}

			return new HttpAdminService(serviceFactory, Local);
		}

		protected override void Config(ConfigSource config) {
			base.Config(config);

			NetworkConfigSource networkConfig = (NetworkConfigSource) config;
			networkConfig.AddAllowedIp("localhost");
			networkConfig.AddAllowedIp("127.0.0.1");
		}

		protected override IServiceConnector CreateConnector() {
			return new HttpServiceConnector("foo", "foo");
		}
	}
}