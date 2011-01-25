// 
//  IStore.cs
//  
//  Author:
//       Antonello Provenzano <antonello@deveel.com>
//       Tobias Downer <toby@mckoi.com>
//  
//  Copyright (c) 2009 Deveel
// 
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections;
using System.IO;

namespace Deveel.Data.Store {
	/// <summary>
	/// A store is a resource where areas can be allocated and freed to 
	/// store information (a memory allocator).
	/// </summary>
	/// <remarks>
	/// A store can be backed by a file system or main memory, or any type 
	/// of information storage mechanism that allows the creation, modification 
	/// and fast lookup of blocks of information.
	/// <para>
	/// Some characteristics of implementations of Store may be separately
	/// specified.  For example, a file based store that is intended to persistently
	/// store objects may have robustness as a primary requirement.  A main memory
	/// based store, or another type of volatile storage system, may not need to be
	/// sensitive to system crashes or data consistancy requirements across multiple
	/// sessions.
	/// </para>
	/// <para>
	/// Some important assumptions for implementations; The data must not be
	/// changed in any way outside of the methods provided by the methods in the
	/// class. For persistant implementations, the information must remain the same
	/// over invocations, however its often not possible to guarantee this.  At
	/// least, the store should be able to recover to the last check point.
	/// </para>
	/// <para>
	/// This interface is the principle class to implement when porting the database
	/// to different types of storage devices.
	/// </para>
	/// <para>
	/// Note that we use 'long' identifiers to reference areas in the store however
	/// only the first 60 bits of an identifer will be used unless we are referencing 
	/// system (the static area is -1) or implementation specific areas.
	/// </para>
	/// </remarks>
	public interface IStore {
		/// <summary>
		/// Allocates a block of memory in the store of the specified size 
		/// and returns an <see cref="IAreaWriter"/> object that can be used 
		/// to initialize the contents of the area.
		/// </summary>
		/// <param name="size">The amount of memory to allocate.</param>
		/// <remarks>
		/// Note that an area in the store is undefined until the <see cref="IAreaWriter.Finish"/>
		/// method is called in <see cref="IAreaWriter"/>.
		/// </remarks>
		/// <returns>
		/// Returns an <see cref="IAreaWriter"/> object that allows the area to be setup.
		/// </returns>
		/// <exception cref="System.IO.IOException">
		/// If not enough space available to create the area or the store is Read-only.
		/// </exception>
		IAreaWriter CreateArea(long size);

		/// <summary>
		/// Deletes an area that was previously allocated by the <see cref="CreateArea"/>
		/// method by the area id.
		/// </summary>
		/// <param name="id">The identifier of the area to delete.</param>
		/// <remarks>
		/// Once an area is deleted the resources may be reclaimed. The behaviour of this 
		/// method is undefined if the id doesn't represent a valid area.
		/// </remarks>
		/// <exception cref="IOException">
		/// If the id is invalid or the area can not otherwise by deleted.
		/// </exception>
		void DeleteArea(long id);


		/// <summary>
		/// Returns a <see cref="Stream"/> that allows for the area with the 
		/// given identifier to be Read sequentially.
		/// </summary>
		/// <param name="id">The identifier of the area to Read, or -1 for the 64 byte 
		/// fixed area in the store.</param>
		/// <remarks>
		/// The behaviour of this method is undefined if the id doesn't represent 
		/// a valid area.
		/// <para>
		/// When 'id' is -1 then a fixed static area (64 bytes in size) in the store
		/// is returned.  The fixed area can be used to store important static
		/// information.
		/// </para>
		/// </remarks>
		/// <returns>
		/// Returns a <see cref="Stream"/> that can be used <b>only</b> to Read the 
		/// area from the start.
		/// </returns>
		/// <exception cref="IOException">
		/// If the id is invalid or the area can not otherwise be accessed.
		/// </exception>
		Stream GetAreaInputStream(long id);


		/// <summary>
		/// Returns an object that allows for the contents of an area (represented 
		/// by the <paramref name="id"/> parameter) to be Read.
		/// </summary>
		/// <param name="id">The identifier of the area to Read, or -1 for a 64 byte 
		/// fixed area in the store.</param>
		/// <remarks>
		/// The behaviour of this method is undefined if the id doesn't represent a valid area.
		/// <para>
		/// When <paramref name="id"/> is -1 then a fixed area (64 bytes in size) in the store is 
		/// returned. The fixed area can be used to store important static information.
		/// </para>
		/// </remarks>
		/// <returns>
		/// Returns an <see cref="IArea"/> object that allows access to the part of the store.
		/// </returns>
		/// <exception cref="IOException">
		/// If the id is invalid or the area can not otherwise be accessed.
		/// </exception>
		IArea GetArea(long id);


		/// <summary>
		/// Returns an object that allows for the contents of an area (represented 
		/// by the <paramref name="id"/> parameter) to be Read and written.
		/// </summary>
		/// <param name="id">The identifier of the area to access, or -1 for a 64 byte 
		/// fixed area in the store.</param>
		/// <remarks>
		/// The behaviour of this method is undefined if the id doesn't represent a valid area.
		/// <para>
		/// When <paramref name="id"/> is -1 then a fixed area (64 bytes in size) in the store 
		/// is returned. The fixed area can be used to store important static information.
		/// </para>
		/// </remarks>
		/// <returns>
		/// Returns a <see cref="IMutableArea"/> object that allows access to the part of the store.
		/// </returns>
		/// <exception cref="IOException">
		/// If the id is invalid or the area can not otherwise be accessed.
		/// </exception>
		IMutableArea GetMutableArea(long id);

		// ---------- Check Point Locking ----------

		/// <summary>
		/// This method is called before the start of a sequence of Write commands 
		/// between consistant states of some data structure represented by the store.
		/// </summary>
		/// <remarks>
		/// This Lock mechanism is intended to inform the store when it is not safe to
		/// <see cref="CheckPoint">checkpoint</see> the data in a log, ensuring that no 
		/// partial updates are committed to a transaction log and the data can be 
		/// restored in a consistant manner.
		/// <para>
		/// If the store does not implement a check point log or is otherwise not
		/// interested in consistant states of the data, then it is not necessary for
		/// this method to do anything.
		/// </para>
		/// <para>
		/// This method prevents a check point from happening during some sequence of
		/// operations. This method should not Lock unless a check point is in progress.
		/// This method does not prevent concurrent writes to the store.
		/// </para>
		/// </remarks>
		/// <seealso cref="UnlockForWrite"/>
		void LockForWrite();

		/// <summary>
		/// This method is called after the end of a sequence of Write commands 
		/// between consistant states of some data structure represented by the store.
		/// </summary>
		/// <remarks>
		/// See the <see cref="LockForWrite"/> method for a further description of the 
		/// operation of this locking mechanism.
		/// </remarks>
		/// <seealso cref="LockForWrite"/>
		void UnlockForWrite();

		/// <summary>
		/// Check point all the updates on this store up to the current time.
		/// </summary>
		/// <remarks>
		/// When this method returns, there is an implied guarantee that when the store 
		/// is next invocated that at least the data written to the store up to this
		/// point is available from the store.
		/// <para>
		/// This method will block if there is a Write Lock on the store (see <see cref="LockForWrite"/>).
		/// </para>
		/// <para>
		/// If the implented store is not interested in maintaining the consistancy of
		/// the information between invocations then it is not necessary for this
		/// method to do anything.
		/// </para>
		/// </remarks>
		/// <exception cref="System.Threading.ThreadInterruptedException">
		/// If check point interrupted (should only happen under exceptional circumstances).
		/// </exception>
		/// <exception cref="IOException">
		/// If check point failed because of an IO error.
		/// </exception>
		void CheckPoint();

		// ---------- Diagnostic ----------

		/// <summary>
		/// Indicates if the store was closed cleanly last time was accessed.
		/// </summary>
		/// <returns>
		/// Returns <b>true</b> if the store was closed cleanly last time it was
		/// accessed or <b>false</b> otherwise.
		/// </returns>
		/// <remarks>
		/// This is important information that may need to be considered when 
		/// reading information from the store. This is typically used to issue 
		/// a scan on the data in the store when it is not closed cleanly.
		/// </remarks>
		bool LastCloseClean();


//		/// <summary>
//		/// Returns a complete list of pointers to all areas in the <see cref="Store"/> 
//		/// as <see cref="long"/> objects sorted from lowest pointer to highest.
//		/// </summary>
//		/// <remarks>
//		/// This should be used for diagnostics only because it may be difficult for 
//		/// this to be generated with some implementations.  It is useful in a repair 
//		/// tool to determine if a pointer is valid or not.
//		/// </remarks>
//		/// <returns>
//		/// Returns an implementation of <see cref="IList"/> that contains all the pointers
//		/// (as <see cref="long"/>) to the areas from the lowest to the highest.
//		/// </returns>
//		IList GetAllAreas();
	}
}