﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Deveel.Data.Net {
	public abstract class BlockServer : IService {
		private long serverGuid;
		private volatile int blockCount = 0;
		private long lastBlockId = -1;
		private bool disposed;
		private readonly IServiceConnector connector;
		private ErrorStateException errorState;
		
		private readonly Dictionary<long, BlockContainer> blockContainerCache;
		private readonly LinkedList<BlockContainer> blockContainerAccessList;
		private readonly LinkedList<BlockContainer> blocksPendingFlush;
		private Timer blockFlushTimer;
		private readonly object pathLock = new object();
		
		private readonly object processLock = new object();
		private readonly object blockUploadLock = new object();
		private int processId = 0;
		private Timer sendBlockTimer;
		
		protected BlockServer(IServiceConnector connector) {
			this.connector = connector;
			
			blockContainerCache = new Dictionary<long, BlockContainer>(5279);
			blockContainerAccessList = new LinkedList<BlockContainer>();
			blocksPendingFlush = new LinkedList<BlockContainer>();
		}
		
		public ServiceType ServiceType {
			get { return ServiceType.Block; }
		}
		
		public IMessageProcessor Processor {
			get { throw new NotImplementedException(); }
		}
		
		private void Dispose(bool disposing) {
			if (!disposed) {
				OnDispose(disposing);
				
				if (disposing) {
					lock (pathLock) {
						blockContainerCache.Clear();
						blocksPendingFlush.Clear();
						blockContainerAccessList.Clear();
					}
				
					blockCount = 0;
					lastBlockId = -1;
				}
				
				//TODO:
				disposed = true;
			}
		}
		
		private void CheckErrorState() {
			if (errorState != null)
				throw errorState;
		}
		
		private void SetErrorState(Exception e) {
			errorState = new ErrorStateException(e);
		}
		
		private BlockContainer FetchBlockContainer(long block_id) {
			// Check for stop state,
			CheckErrorState();

			BlockContainer container;

			lock (pathLock) {
				// Look up the block container in the map,
				// If the container not in the map, create it and put it in there,
				if (!blockContainerCache.TryGetValue(block_id, out container)) {
					container = LoadBlock(block_id);
					// Put it in the map,
					blockContainerCache[block_id] = container;
				}

				// We manage a small list of containers that have been accessed ordered
				// by last access time.

				// Iterate through the list. If we discover the BlockContainer recently
				// accessed we move it to the front.
				LinkedListNode<BlockContainer> node = blockContainerAccessList.First;
				while (node != null) {
					BlockContainer bc = node.Value;
					if (bc == container) {
						// Found, so move it to the front,
						blockContainerAccessList.Remove(bc);
						blockContainerAccessList.AddFirst(container);
						// Return the container,
						return container;
					}
					node = node.Next;
				}

				// The container isn't found on the access list, so we need to add
				// it.

				// If the size of the list is over some threshold, we clear out the
				// oldest entry and close it,
				int listSize = blockContainerAccessList.Count;
				if (listSize > 32) {
					blockContainerAccessList.Last.Value.Close();
					blockContainerAccessList.RemoveLast();
				}

				// Add to the list,
				container.Open();
				blockContainerAccessList.AddFirst(container);

				// And return the container,
				return container;
			}
		}
		
		private void ScheduleBlockFlush(BlockContainer container, int delay) {
			lock (pathLock) {
				if (!blocksPendingFlush.Contains(container)) {
					blocksPendingFlush.AddFirst(container);
					blockFlushTimer = new Timer(FlushBlock, container, delay, delay);
				}
			}
		}
		
		private void FlushBlock(object state) {
			BlockContainer container = (BlockContainer)state;
			lock (pathLock) {
				blocksPendingFlush.Remove(container);
				try {
					container.Flush();
				} catch (IOException e) {
					// We log the warning, but otherwise ignore any IO Error on a
					// file synchronize.
					//TODO: WARN log ...
				}
			}
		}
		
		private void WriteBlockPart(long blockId, long pos, int storeType, byte[] buffer, int length) {
			// Make sure this process is exclusive
			lock (blockUploadLock) {
				try {
					OnWriteBlockPart(blockId, pos, storeType, buffer, length);
				} catch (IOException e) {
					throw new ApplicationException("IO Error: " + e.Message);
				}
			}

		}
		
		private void CompleteBlockWrite(long blockId, int storeType) {
			// Make sure this process is exclusive
			lock (blockUploadLock) {
				OnCompleteBlockWrite(blockId, storeType);
			}

			// Update internal state as appropriate,
			lock (pathLock) {
				BlockContainer container = LoadBlock(blockId);
				blockContainerCache[blockId] = container;
				if (blockId > lastBlockId) {
					lastBlockId = blockId;
				}
				++blockCount;
			}

		}
		
		protected void SetGuid(long value) {
			serverGuid = value;
		}
		
		protected virtual void OnInit() {
		}
		
		protected virtual void OnDispose(bool disposing) {
		}
		
		protected virtual void OnCompleteBlockWrite(long blockId, int storeType) {
		}
		
		protected virtual void OnWriteBlockPart(long blockId, long pos, int storeType, byte[] buffer, int length) {
			
		}
		
		protected abstract BlockContainer LoadBlock(long blockId);
		
		protected abstract long[] ListBlocks();
		
		public void Init() {
			OnInit();
			
			// Read in all the blocks in and populate the map,
			long[] blocks = ListBlocks();
			long inLastBlockId = -1;

			lock (pathLock) {
				foreach (long blockId in blocks) {
					BlockContainer container = LoadBlock(blockId);
					blockContainerCache[blockId] = container;
					if (blockId > inLastBlockId) {
						inLastBlockId = blockId;
					}
				}
			}

			// Discover the block count,
			blockCount = blocks.Length;
			// The latest block on this server,
			this.lastBlockId = inLastBlockId;
		}
		
		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		
		#region BlockServerMessageProcessor
		
		private sealed class BlockServerMessageProcessor : IMessageProcessor {
			private readonly BlockServer server;
			
			public BlockServerMessageProcessor(BlockServer server) {
				this.server = server;
			}
			
			private void CloseContainers(Dictionary<long, BlockContainer> touched) {
				foreach(KeyValuePair<long, BlockContainer> c in touched) {
					//TODO: DEBUG log ..
					c.Value.Close();
				}
			}
			
			private BlockContainer GetBlock(IDictionary<long, BlockContainer> touched, long blockId) {
				BlockContainer b;
				if (!touched.TryGetValue(blockId, out b)) {
					b = server.FetchBlockContainer(blockId);
					bool created = b.Open();
					if (created) {
						++server.blockCount;
						server.lastBlockId = blockId;
					}
					touched[blockId] = b;
					//TODO: DEBUG log ...
				}
				return b;
			}
			
			private void WriteToBlock(Dictionary<long, BlockContainer> touched, DataAddress address, byte[] buf, int off, int len) {
				// The block being written to,
				long blockId = address.BlockId;
				// The data identifier,
				int dataId = address.DataId;

				// Fetch the block container,
				BlockContainer container = GetBlock(touched, blockId);
				// Write the data,
				container.Write(dataId, buf, off, len);

				// Schedule the block to flushed 5 seconds after a write
				server.ScheduleBlockFlush(container, 5000);
			}
			
			private NodeSet ReadFromBlock(IDictionary<long, BlockContainer> touched, DataAddress address) {
				// The block being written to,
				long blockId = address.BlockId;
				// The data identifier,
				int dataId = address.DataId;

				// Fetch the block container,
				BlockContainer container = GetBlock(touched, blockId);
				// Read the data,
				return container.GetNodeSet(dataId);
			}
			
			private void DeleteNodes(IDictionary<long, BlockContainer> touched, DataAddress[] addresses) {
				foreach (DataAddress address in addresses) {
					// The block being removed from,
					long blockId = address.BlockId;
					// The data identifier,
					int dataId = address.DataId;

					// Fetch the block container,
					BlockContainer container = GetBlock(touched, blockId);
					// Remove the data,
					container.Delete(dataId);
					// Schedule the block to be file synch'd 5 seconds after a write
					server.ScheduleBlockFlush(container, 5000);
				}
			}
			
			private long SendBlockTo(long blockId, ServiceAddress destination,
									 long destServerGuid,
									 ServiceAddress managerAddress) {
				lock (server.processLock) {
					long processId = server.processId;
					server.processId = server.processId + 1;
					SendBlockInfo info = new SendBlockInfo(processId, blockId,
													  destination,
													  destServerGuid,
													  managerAddress);
					// Schedule the process to happen immediately (or as immediately as
					// possible).
					server.sendBlockTimer = new Timer(SendBlock, info, 0, Timeout.Infinite);

					return processId;
				}
			}
			
			private void SendBlock(object state) {
				SendBlockInfo info = (SendBlockInfo)state;
				
				BlockContainer container = server.FetchBlockContainer(info.blockId);
				if (!container.Exists)
					return;
				
				// Connect to the destination service address,
				IMessageProcessor p = server.connector.Connect(info.destination, ServiceType.Block);
				
				try {
					// If the block was accessed less than 5 minutes ago, we don't allow
					// the copy to happen,
					if (info.blockId == server.lastBlockId) {
						//TODO: INFO log ...
						return;
					} else if (container.LastWriteTime > 
					           DateTime.Now.AddMilliseconds(-(6 * 60 * 1000))) {
						// Won't copy a block that was written to within the last 6 minutes,
						//TODO: INFO log ...
						return;
					}
					
					MessageStream inputStream, outputStream;

					// If the block does exist, push it over,
					byte[] buf = new byte[16384];
					int pos = 0;
					using(Stream input = container.OpenInputStream()) {
						int read;
						while ((read = input.Read(buf, 0, buf.Length)) != 0) {
							outputStream = new MessageStream(8);
							outputStream.AddMessage("sendBlockPart", info.blockId, pos, 
							                        container.Type, buf, read);
							// Process the message,
							inputStream = p.Process(outputStream);
							// Get the input iterator,
							foreach(Message m in inputStream) {
								if (m.IsError) {
									//TODO: ERROR log ...
									return;
								}
							}
	
							pos += read;
						}
					}
					
					// Send the 'complete' command,
					outputStream = new MessageStream(8);
					outputStream.AddMessage("sendBlockComplete", info.blockId, container.Type);
					// Process the message,
					inputStream = p.Process(outputStream);
					// Get the input iterator,
					foreach(Message m in inputStream) {
						if (m.IsError) {
							//TODO: ERROR log ...
							return;
						}
					}

					// Tell the manager server about this new block mapping,
					IMessageProcessor mp = server.connector.Connect(info.managerAddress, ServiceType.Manager);
					outputStream = new MessageStream(8);
					outputStream.AddMessage("addBlockServerMapping", info.blockId, info.destServerGuid);
					
					//TODO: DEBUG log ...

					// Process the message,
					inputStream = mp.Process(outputStream);
					// Get the input iterator,
					foreach(Message m in inputStream) {
						if (m.IsError) {
							//TODO: ERROR log ...
							return;
						}
					}

				} catch (IOException e) {
					//TODO: ERROR log ...
				}
			}
			
			private long GetBlockChecksum(IDictionary<long, BlockContainer> touched, long blockId) {
				// Fetch the block container,
				BlockContainer container = GetBlock(touched, blockId);
				// Calculate the checksum value,
				return container.CreateChecksum();
			}
			
			public MessageStream Process(MessageStream messageStream) {
				// The map of containers touched,
				Dictionary<long, BlockContainer> containersTouched = new Dictionary<long, BlockContainer>();
				// The nodes fetched in this message,
				List<long> readNodes = null;
				
				MessageStream responseStream = new MessageStream(32);
				
				foreach(Message m in messageStream) {
					try {
						server.CheckErrorState();
						
						switch(m.Name) {
							case "bindWithManager":
							case "unbindWithManager":
								//TODO: is this needed for a block server?
								responseStream.AddMessage("R", 1L);
								break;
							case "serverGUID":
								responseStream.AddMessage("R", server.serverGuid);
								break;
							case "writeToBlock": {
								DataAddress address = (DataAddress)m[0];
								byte[] buffer = (byte[])m[1];
								int offset = (int)m[2];
								int length = (int)m[3];
								WriteToBlock(containersTouched, address, buffer, offset, length);
								responseStream.AddMessage("R", 1L);
								break;
							}
							case "readFromBlock": {
								if (readNodes == null)
									readNodes = new List<long>();
								
								DataAddress address = (DataAddress)m[0];
								if (!readNodes.Contains(address.Value)) {
									NodeSet nodeSet = ReadFromBlock(containersTouched, address);
									responseStream.AddMessage("R", nodeSet);
									foreach (long node in nodeSet.NodeIds)
										readNodes.Add(node);
								}
								break;
							}
							case "rollbackNodes": {
								DataAddress[] addresses = (DataAddress[])m[0];
								DeleteNodes(containersTouched, addresses);
								responseStream.AddMessage("R", 1L);
								break;	
							}
							case "deleteBlock": {
								long blockId = (long)m[0];
								//TODO: ...
								responseStream.AddMessage("R", 1L);
								break;	
							}
							case "blockSetReport": {
								long[] blockIds = server.ListBlocks();
								responseStream.AddMessage("R", server.serverGuid, blockIds);
								break;
							}
							case "blockChecksum": {
								long blockId = (long)m[0];
								long checksum = GetBlockChecksum(containersTouched, blockId);
								responseStream.AddMessage("R", checksum);
								break;	
							}
							case "sendBlockTo": {
								// Returns immediately. There's currently no way to determine
								// when this process will happen or if it will happen.
								long blockId = (long)m[0];
								ServiceAddress destAddress = (ServiceAddress)m[1];
								long destServerGuid = (long)m[2];
								ServiceAddress managerAddress = (ServiceAddress)m[3];
								long processId = SendBlockTo(blockId, destAddress, destServerGuid, managerAddress);
								responseStream.AddMessage("R", processId);
								break;
							}
							case "sendBlockPart": {
								long blockId = (long)m[0];
								long pos = (long)m[1];
								int storeType = (int)m[2];
								byte[] buffer = (byte[])m[3];
								int length = (int)m[4];
								server.WriteBlockPart(blockId, pos, storeType, buffer, length);
								responseStream.AddMessage("R", 1L);
								break;	
							}
							case "sendBlockComplete": {
								long blockId = (long)m[0];
								int storeType = (int)m[1];
								server.CompleteBlockWrite(blockId, storeType);
								responseStream.AddMessage("R", 1L);
								break;
							}
							default:
								throw new ApplicationException("Unknown message name: " + m.Name);
						}
					} catch(OutOfMemoryException e) {
						//TODO: ERROR log ...
						server.SetErrorState(e);
						throw;
					} catch (Exception e) {
						//TODO: ERROR log ...
						responseStream.AddErrorMessage(new ServiceException(e));
					}
				}
				
				// Release any containers touched,
				try {
					CloseContainers(containersTouched);
				} catch (Exception e) {
					//TODO: ERROR log ...
				}
				
				return responseStream;
			}
		}
		
		#region SendBlockInfo
		
		private struct SendBlockInfo {
			public SendBlockInfo(long processId, long blockId, ServiceAddress destination, 
			                     long destServerGuid, ServiceAddress managerAddress) {
				this.processId = processId;
				this.blockId = blockId;
				this.destination = destination;
				this.destServerGuid = destServerGuid;
				this.managerAddress = managerAddress;
			}

			public long processId;
			public long blockId;
			public ServiceAddress destination;
			public long destServerGuid;
			public ServiceAddress managerAddress;
		}
		
		#endregion
		
		#endregion
		
		#region BlockContainer
		
		protected sealed class BlockContainer : IBlockStore, IComparable<BlockContainer> {
			public IBlockStore blockStore;
			public readonly long blockId;
			private DateTime lastWrite = DateTime.MinValue;
			private int lockCount = 0;
			private int type;
			
			internal BlockContainer(long blockId, IBlockStore blockStore) {
				this.blockId = blockId;
				this.blockStore = blockStore;
				type = blockStore.Type;
			}
			
			public DateTime LastWriteTime {
				get { return lastWrite; }
			}
			
			public IBlockStore BlockStore {
				get { return blockStore; }
			}
			
			public long BlockId {
				get { return blockId; }
			}
			
			public bool Exists {
				get {
					lock(this) {
						return blockStore.Exists;
					}
				}
			}
			
			public int Type {
				get{ return type; }
			}
			
			public bool Open() {
				lock (this) {
					if (lockCount == 0) {
						++lockCount;
						return blockStore.Open();
					}
					++lockCount;
					return false;
				}
			}
			
			public void Close() {
				lock (this) {
					if (--lockCount == 0)
						blockStore.Close();
				}
			}
			
			public void ChangeStore(IBlockStore newStore) {
				lock (this) {
					if (lockCount > 0) {
						blockStore.Close();
						newStore.Open();
					}
					blockStore = newStore;
				}
			}
			
			public void Write(int dataId, byte[] buffer, int offset, int length) {
				lock (this) {
					blockStore.Write(dataId, buffer, offset, length);
					lastWrite = DateTime.Now;
				}
			}
			
			public int Read(int dataId, byte[] buffer, int offset, int length) {
				lock(this) {
					return blockStore.Read(dataId, buffer, offset, length);
				}
			}
			
			public Stream OpenInputStream() {
				lock(this) {
					return blockStore.OpenInputStream();
				}
			}
			
			public void Delete(int dataId) {
				lock (this) {
					blockStore.Delete(dataId);
					lastWrite = DateTime.Now;
				}
			}
			
			public NodeSet GetNodeSet(int dataId) {
				lock (this) {
					return blockStore.GetNodeSet(dataId);
				}
			}
			
			public void Flush() {
				lock (this) {
					blockStore.Flush();
				}
			}
			
			public long CreateChecksum() {
				lock (this) {
					return blockStore.CreateChecksum();
				}
			}
			
			public int CompareTo(BlockContainer other) {
				return blockId.CompareTo(other.blockId);
			}
		}
		
		#endregion
	}
}