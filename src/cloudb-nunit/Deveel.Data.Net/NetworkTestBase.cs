<<<<<<< HEAD
﻿using System;
using System.IO;
using System.Threading;

using Deveel.Data.Configuration;
using Deveel.Data.Diagnostics;

using NUnit.Framework;

namespace Deveel.Data.Net {
	[TestFixture]
	public abstract class NetworkTestBase {
		private NetworkProfile networkProfile;
		private AdminService adminService;
		private readonly NetworkStoreType storeType;
		private string path;

		private static readonly AutoResetEvent SetupEvent = new AutoResetEvent(true);

		protected NetworkTestBase(NetworkStoreType storeType) {
			this.storeType = storeType;
		}

		protected AdminService AdminService {
			get { return adminService; }
		}

		protected string TestPath {
			get {
				if (path == null)
					path = Path.Combine(Environment.CurrentDirectory, "base");
				return path;
			}
		}

		protected abstract IServiceAddress LocalAddress { get; }

		protected virtual void Config(ConfigSource config) {
			if (storeType == NetworkStoreType.FileSystem) {
				if (Directory.Exists(TestPath))
					Directory.Delete(TestPath, true);

				Directory.CreateDirectory(path);

				config.SetValue("node_directory", path);
			}

			config.SetValue("logger." + Logger.NetworkLoggerName + ".type", "simple-console");
			Logger.Init(config);
			Assert.IsInstanceOf(typeof(SimpleConsoleLogger), Logger.Network.BaseLogger);
		}

		protected abstract AdminService CreateAdminService(NetworkStoreType storeType);

		protected abstract IServiceConnector CreateConnector();

		[SetUp]
		public void SetUp() {
			SetupEvent.WaitOne();

			IServiceConnector connector = CreateConnector();

			adminService = CreateAdminService(storeType);
			NetworkConfigSource config = new NetworkConfigSource();
			Config(config);
			adminService.Config = config;
			adminService.Connector = connector;
			adminService.Start();
			networkProfile = new NetworkProfile(connector);

			NetworkConfigSource netConfig = new NetworkConfigSource();
			netConfig.AddNetworkNode(LocalAddress);
			networkProfile.Configuration = netConfig;

			SetupEvent.Set();
		}

		[TearDown]
		public void TearDown() {
			try {
				adminService.Stop();
				adminService.Dispose();

				if (storeType == NetworkStoreType.FileSystem &&
					Directory.Exists(TestPath))
					Directory.Delete(TestPath, true);
			} finally {
				SetupEvent.Set();
			}
		}

		[Test]
		public void StartManager() {
			MachineProfile machine = networkProfile.GetMachineProfile(LocalAddress);
			Assert.IsNotNull(machine);
			Assert.IsNull(networkProfile.ManagerServer);
			Assert.IsFalse(machine.IsManager);
			networkProfile.StartService(LocalAddress, ServiceType.Manager);

			networkProfile.Refresh();

			machine = networkProfile.GetMachineProfile(LocalAddress);
			Assert.IsNotNull(machine);
			Assert.IsNotNull(networkProfile.ManagerServer);
			Assert.IsTrue(machine.IsManager);
		}

		[Test]
		public void StartRoot() {
			StartManager();

			MachineProfile machine = networkProfile.GetMachineProfile(LocalAddress);
			Assert.IsNotNull(machine);
			Assert.IsFalse(machine.IsRoot);
			networkProfile.StartService(LocalAddress, ServiceType.Root);
			networkProfile.RegisterRoot(LocalAddress);
		}

		[Test]
		public void StartBlock() {
			StartManager();

			MachineProfile machine = networkProfile.GetMachineProfile(LocalAddress);
			Assert.IsNotNull(machine);
			Assert.IsFalse(machine.IsBlock);
			networkProfile.StartService(LocalAddress, ServiceType.Block);
			networkProfile.RegisterBlock(LocalAddress);
		}

		[Test]
		public void StartAllServices() {
			StartManager();

			networkProfile.Refresh();

			MachineProfile machine = networkProfile.GetMachineProfile(LocalAddress);
			Assert.IsNotNull(machine);
			Assert.IsFalse(machine.IsRoot);
			networkProfile.StartService(LocalAddress, ServiceType.Root);
			networkProfile.RegisterRoot(LocalAddress);

			networkProfile.Refresh();

			machine = networkProfile.GetMachineProfile(LocalAddress);
			Assert.IsNotNull(machine);
			Assert.IsFalse(machine.IsBlock);
			networkProfile.StartService(LocalAddress, ServiceType.Block);
			networkProfile.RegisterBlock(LocalAddress);
		}

		[Test]
		public void StartAndStopManager() {
			StartManager();

			MachineProfile machine = networkProfile.GetMachineProfile(LocalAddress);
			Assert.IsNotNull(machine);
			Assert.IsTrue(machine.IsManager);

			networkProfile.StopService(LocalAddress, ServiceType.Manager);

			networkProfile.Refresh();

			machine = networkProfile.GetMachineProfile(LocalAddress);
			Assert.IsNotNull(machine);
			Assert.IsFalse(machine.IsManager);
		}

		[Test]
		public void StartAndStopRoot() {
			StartRoot();

			MachineProfile machine = networkProfile.GetMachineProfile(LocalAddress);
			Assert.IsNotNull(machine);
			Assert.IsTrue(machine.IsRoot);

			networkProfile.StopService(LocalAddress, ServiceType.Root);
			networkProfile.Refresh();

			machine = networkProfile.GetMachineProfile(LocalAddress);
			Assert.IsNotNull(machine);
			Assert.IsFalse(machine.IsRoot);
		}

		[Test]
		public void StartAndStopBlock() {
			StartBlock();

			MachineProfile machine = networkProfile.GetMachineProfile(LocalAddress);
			Assert.IsNotNull(machine);
			Assert.IsTrue(machine.IsBlock);

			networkProfile.StopService(LocalAddress, ServiceType.Block);

			networkProfile.Refresh();

			machine = networkProfile.GetMachineProfile(LocalAddress);
			Assert.IsNotNull(machine);
			Assert.IsFalse(machine.IsBlock);
		}

		[Test]
		public void StartAndStopAllServices() {
			StartAllServices();

			//TODO:
		}
	}
=======
﻿using System;
using System.IO;
using System.Threading;

using Deveel.Data.Configuration;
using Deveel.Data.Diagnostics;

using NUnit.Framework;

namespace Deveel.Data.Net {
	[TestFixture]
	public abstract class NetworkTestBase {
		private NetworkProfile networkProfile;
		private AdminService adminService;
		private readonly NetworkStoreType storeType;
		private string path;

		private static readonly AutoResetEvent SetupEvent = new AutoResetEvent(true);

		protected NetworkTestBase(NetworkStoreType storeType) {
			this.storeType = storeType;
		}

		protected AdminService AdminService {
			get { return adminService; }
		}

		protected NetworkProfile NetworkProfile {
			get { return networkProfile; }
		}

		protected string TestPath {
			get {
				if (path == null)
					path = Path.Combine(Environment.CurrentDirectory, "base");
				return path;
			}
		}

		protected abstract IServiceAddress LocalAddress { get; }

		protected virtual void Config(ConfigSource config) {
			if (storeType == NetworkStoreType.FileSystem) {
				if (Directory.Exists(TestPath))
					Directory.Delete(TestPath, true);

				Directory.CreateDirectory(path);

				config.SetValue("node_directory", path);
			}

			config.SetValue("logger." + Logger.NetworkLoggerName + ".type", "simple-console");
			Logger.Init(config);
			Assert.IsInstanceOf(typeof(SimpleConsoleLogger), Logger.Network.BaseLogger);
		}

		protected abstract AdminService CreateAdminService(NetworkStoreType storeType);

		protected abstract IServiceConnector CreateConnector();

		[SetUp]
		public void SetUp() {
			SetupEvent.WaitOne();

			IServiceConnector connector = CreateConnector();

			adminService = CreateAdminService(storeType);
			NetworkConfigSource config = new NetworkConfigSource();
			Config(config);
			adminService.Config = config;
			adminService.Connector = connector;
			adminService.Start();
			networkProfile = new NetworkProfile(connector);

			NetworkConfigSource netConfig = new NetworkConfigSource();
			netConfig.AddNetworkNode(LocalAddress);
			networkProfile.Configuration = netConfig;

			SetupEvent.Set();
		}

		[TearDown]
		public void TearDown() {
			try {
				adminService.Stop();
				adminService.Dispose();

				if (storeType == NetworkStoreType.FileSystem &&
					Directory.Exists(TestPath))
					Directory.Delete(TestPath, true);
			} finally {
				SetupEvent.Set();
			}
		}
	}
>>>>>>> master
}