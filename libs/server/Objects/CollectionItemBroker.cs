﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tsavorite.core;

namespace Garnet.server
{
    /// <summary>
    /// This class brokers collection items for blocking operations.
    /// When a supported blocking command is initiated, RespServerSession will call the GetCollectionItemAsync method
    /// with the desired object type and operation and a list of keys to the desired objects.
    /// When an item is added to a collection, the StorageSession will call the Publish method with the relevant object key
    /// to notify the broker that a new item may be available.
    /// The main loop, in the Start method, listens for published item additions as well as new observers
    /// and notifies the calling method if an item was found.
    /// </summary>
    public class CollectionItemBroker : IDisposable
    {
        // Queue of events to be handled by the main loops
        private readonly AsyncQueue<BrokerEventBase> brokerEventsQueue = new();

        // Mapping of RespServerSession ID (ObjectStoreSessionID) to observer instance
        private readonly ConcurrentDictionary<int, CollectionItemObserver> sessionIdToObserver = new();

        // Mapping of observed keys to queue of observers, by order of subscription
        private readonly Dictionary<byte[], Queue<CollectionItemObserver>> keysToObservers = new(new ByteArrayComparer());

        // Cancellation token for the main loop
        private readonly CancellationTokenSource cts = new();

        // Synchronization event for awaiting main loop to finish
        private readonly ManualResetEventSlim done = new(true);

        private bool disposed = false;
        private bool isStarted = false;
        private bool stop = false;
        private readonly ReaderWriterLockSlim isStartedLock = new();
        private readonly ReaderWriterLockSlim keysToObserversLock = new();
        private readonly ReaderWriterLockSlim sessionIdToObserverLock = new();
        private readonly TimeSpan cleanUpPeriod = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Asynchronously wait for item from list object
        /// </summary>
        /// <param name="keys">Keys of objects to observe</param>
        /// <param name="operation">Type of list operation</param>
        /// <param name="session">Calling session instance</param>
        /// <param name="timeoutInSeconds">Timeout of operation (in seconds, 0 for waiting indefinitely)</param>
        internal async Task<CollectionItemResult> GetListItemAsync(byte[][] keys, ListOperation operation,
            RespServerSession session, double timeoutInSeconds)
        {
            return await GetCollectionItemAsync(keys, GarnetObjectType.List, (byte)operation, session,
                timeoutInSeconds);
        }

        /// <summary>
        /// Asynchronously wait for item from sorted set object
        /// </summary>
        /// <param name="keys">Keys of objects to observe</param>
        /// <param name="operation">Type of sorted set operation</param>
        /// <param name="session">Calling session instance</param>
        /// <param name="timeoutInSeconds">Timeout of operation (in seconds, 0 for waiting indefinitely)</param>
        internal async Task<CollectionItemResult> GetSortedSetItemAsync(byte[][] keys, SortedSetOperation operation,
            RespServerSession session, double timeoutInSeconds)
        {
            return await GetCollectionItemAsync(keys, GarnetObjectType.SortedSet, (byte)operation, session,
                timeoutInSeconds);
        }

        /// <summary>
        /// Notify broker that an item was added to a collection object in specified key
        /// </summary>
        /// <param name="key">Key of the updated collection object</param>
        internal void HandleCollectionUpdate(byte[] key)
        {
            // Check if main loop is started
            isStartedLock.EnterReadLock();
            try
            {
                if (!isStarted) return;

                keysToObserversLock.EnterUpgradeableReadLock();
                try
                {
                    // Check if there are any observers to specified key
                    if (!keysToObservers.TryGetValue(key, out var observers)) return;

                    if (observers.Count == 0)
                    {
                        keysToObserversLock.EnterWriteLock();
                        try
                        {
                            keysToObservers.Remove(key);
                            return;
                        }
                        finally
                        {
                            keysToObserversLock.ExitWriteLock();
                        }
                    }

                    // Add collection updated event to queue
                    brokerEventsQueue.Enqueue(new CollectionUpdatedEvent(key));
                }
                finally
                {
                    keysToObserversLock.ExitUpgradeableReadLock();
                }
            }
            finally
            {
                isStartedLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Notify broker that a RespServerSession object is being disposed
        /// </summary>
        /// <param name="session">The disposed session</param>
        internal void HandleSessionDisposed(RespServerSession session)
        {
            CollectionItemObserver observer;

            // Try to remove session ID from mapping & get the observer object for the specified session, if exists
            sessionIdToObserverLock.EnterWriteLock();
            try
            {
                if (!sessionIdToObserver.TryRemove(session.ObjectStoreSessionID, out observer))
                    return;
            }
            finally
            {
                sessionIdToObserverLock.ExitWriteLock();
            }

            // Change observer status to reflect that its session has been disposed
            observer.HandleSessionDisposed();
        }

        /// <summary>
        /// Get the key's observer queue if exists and not empty
        /// If key exists and queue is empty, remove the key
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="observers">Observers queue</param>
        /// <returns>True if observers queue exists and not empty</returns>
        private bool TryGetObserverQueue(byte[] key, out Queue<CollectionItemObserver> observers)
        {
            keysToObserversLock.EnterUpgradeableReadLock();
            try
            {
                // Check if there are any observers to specified key
                if (!keysToObservers.TryGetValue(key, out observers)) return false;

                if (observers.Count == 0)
                {
                    keysToObserversLock.EnterWriteLock();
                    try
                    {
                        keysToObservers.Remove(key);
                        return false;
                    }
                    finally
                    {
                        keysToObserversLock.ExitWriteLock();
                    }
                }
            }
            finally
            {
                keysToObserversLock.ExitUpgradeableReadLock();
            }

            return true;
        }

        /// <summary>
        /// Asynchronously wait for item from collection object
        /// </summary>
        /// <param name="keys">Keys of objects to observe</param>
        /// <param name="objectType">Type of object to observe</param>
        /// <param name="operation">Type of operation</param>
        /// <param name="session">Calling session instance</param>
        /// <param name="timeoutInSeconds">Timeout of operation (in seconds, 0 for waiting indefinitely)</param>
        /// <returns></returns>
        private async Task<CollectionItemResult> GetCollectionItemAsync(byte[][] keys, GarnetObjectType objectType,
            byte operation, RespServerSession session, double timeoutInSeconds)
        {
            // Create the new observer object
            var observer = new CollectionItemObserver(session, objectType, operation);

            // Check if main loop has started, if not, start the main loop
            isStartedLock.EnterUpgradeableReadLock();
            try
            {
                if (!isStarted)
                {
                    isStartedLock.EnterWriteLock();
                    try
                    {
                        _ = Task.Run(Start);
                        isStarted = true;
                    }
                    finally
                    {
                        isStartedLock.ExitWriteLock();
                    }
                }

                // Add the session ID to observer mapping
                sessionIdToObserverLock.EnterWriteLock();
                try
                {
                    sessionIdToObserver.TryAdd(session.ObjectStoreSessionID, observer);
                }
                finally
                {
                    sessionIdToObserverLock.ExitWriteLock();
                }

                // Add a new observer event to the event queue
                brokerEventsQueue.Enqueue(new NewObserverEvent(observer, keys));
            }
            finally
            {
                isStartedLock.ExitUpgradeableReadLock();
            }

            var timeout = timeoutInSeconds == 0
                ? TimeSpan.FromMilliseconds(-1)
                : TimeSpan.FromSeconds(timeoutInSeconds);

            try
            {
                // Wait for either the result found notification or the timeout to expire
                await observer.ResultFoundSemaphore.WaitAsync(timeout, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }

            sessionIdToObserverLock.EnterWriteLock();
            try
            {
                sessionIdToObserver.TryRemove(observer.Session.ObjectStoreSessionID, out _);
            }
            finally
            {
                sessionIdToObserverLock.ExitWriteLock();
            }

            // Check if observer is still waiting for result
            if (observer.Status == ObserverStatus.WaitingForResult)
            {
                // Try to set the observer result to an empty one
                observer.HandleSetResult(CollectionItemResult.Empty);
            }

            return observer.Result;
        }

        /// <summary>
        /// Calls the appropriate method based on the broker event type
        /// </summary>
        /// <param name="brokerEvent"></param>
        private void HandleBrokerEvent(BrokerEventBase brokerEvent)
        {
            switch (brokerEvent)
            {
                case NewObserverEvent noe:
                    InitializeObserver(noe.Observer, noe.Keys);
                    return;
                case CollectionUpdatedEvent cue:
                    TryAssignItemFromKey(cue.Key);
                    if (sessionIdToObserver.IsEmpty && brokerEventsQueue.Count == 0)
                        TryStop();
                    return;
            }
        }

        /// <summary>
        /// Handles a new observer
        /// </summary>
        /// <param name="observer">The new observer instance</param>
        /// <param name="keys">Keys observed by the new observer</param>
        private void InitializeObserver(CollectionItemObserver observer, byte[][] keys)
        {
            // This lock is for synchronization with incoming collection updated events 
            keysToObserversLock.EnterWriteLock();
            try
            {
                // Iterate over the keys in order, set the observer's result if collection in key contains an item
                foreach (var key in keys)
                {
                    // If the key already has a non-empty observer queue, it does not have an item to retrieve
                    // Otherwise, try to retrieve next available item
                    if ((keysToObservers.ContainsKey(key) && keysToObservers[key].Count > 0) ||
                        !TryGetNextItem(key, observer.Session.storageSession, observer.ObjectType, observer.Operation,
                            out _, out var nextItem)) continue;

                    // An item was found - set the observer result and return
                    sessionIdToObserverLock.EnterWriteLock();
                    try
                    {
                        sessionIdToObserver.TryRemove(observer.Session.ObjectStoreSessionID, out _);
                    }
                    finally
                    {
                        sessionIdToObserverLock.ExitWriteLock();
                    }
                    observer.HandleSetResult(new CollectionItemResult(key, nextItem));
                    return;
                }

                // No item was found, enqueue new observer in every observed keys queue
                foreach (var key in keys)
                {
                    if (!keysToObservers.ContainsKey(key))
                        keysToObservers.Add(key, new Queue<CollectionItemObserver>());

                    keysToObservers[key].Enqueue(observer);
                }
            }
            finally
            {
                keysToObserversLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Try to assign item available (if exists) with next ready observer in queue
        /// </summary>
        /// <param name="key">Key of collection from which to assign item</param>
        /// <returns>True if successful in assigning item</returns>
        private bool TryAssignItemFromKey(byte[] key)
        {
            // If queue doesn't exist for key or is empty, nothing to do
            if (!TryGetObserverQueue(key, out var observers)) return false;

            // Peek at next observer in queue
            while (observers.TryPeek(out var observer))
            {
                // If observer is not waiting for result, dequeue it and continue to next observer in queue
                if (observer.Status != ObserverStatus.WaitingForResult)
                {
                    observers.Dequeue();
                    continue;
                }

                observer.ObserverStatusLock.EnterUpgradeableReadLock();
                try
                {
                    // If observer is not waiting for result, dequeue it and continue to next observer in queue
                    if (observer.Status != ObserverStatus.WaitingForResult)
                    {
                        observers.Dequeue();
                        continue;
                    }

                    // Try to get next available item from object stored in key
                    if (!TryGetNextItem(key, observer.Session.storageSession, observer.ObjectType, observer.Operation,
                            out var currCount, out var nextItem))
                    {
                        // If unsuccessful getting next item but there is at least one item in the collection,
                        // continue to next observer in the queue, otherwise return
                        if (currCount > 0) continue;

                        // Call this method to remove key if observer queue is now empty
                        TryGetObserverQueue(key, out _);

                        return false;
                    }

                    // Dequeue the observer, and set the observer's result
                    observer = observers.Dequeue();

                    sessionIdToObserverLock.EnterWriteLock();
                    try
                    {
                        sessionIdToObserver.TryRemove(observer.Session.ObjectStoreSessionID, out _);
                    }
                    finally
                    {
                        sessionIdToObserverLock.ExitWriteLock();
                    }

                    observer.HandleSetResult(new CollectionItemResult(key, nextItem));

                    // Call this method to remove key if observer queue is now empty
                    TryGetObserverQueue(key, out _);

                    return true;
                }
                finally
                {
                    observer?.ObserverStatusLock.ExitUpgradeableReadLock();
                }
            }

            // Call this method to remove key if observer queue is now empty
            TryGetObserverQueue(key, out _);

            return false;
        }

        /// <summary>
        /// Try to get next available item from list object
        /// </summary>
        /// <param name="listObj">List object</param>
        /// <param name="operation">List operation</param>
        /// <param name="nextItem">Item retrieved</param>
        /// <returns>True if found available item</returns>
        private static bool TryGetNextListItem(ListObject listObj, byte operation, out byte[] nextItem)
        {
            nextItem = default;

            // If object has no items, return
            if (listObj.LnkList.Count == 0) return false;

            // Get the next object according to operation type
            switch ((ListOperation)operation)
            {
                case ListOperation.BRPOP:
                    nextItem = listObj.LnkList.Last!.Value;
                    listObj.LnkList.RemoveLast();
                    break;
                case ListOperation.BLPOP:
                    nextItem = listObj.LnkList.First!.Value;
                    listObj.LnkList.RemoveFirst();
                    break;
                default:
                    return false;
            }

            listObj.UpdateSize(nextItem, false);

            return true;
        }

        /// <summary>
        /// Try to get next available item from sorted set object
        /// </summary>
        /// <param name="sortedSetObj">Sorted set object</param>
        /// <param name="operation">Sorted set operation</param>
        /// <param name="nextItem">Item retrieved</param>
        /// <returns>True if found available item</returns>
        private static bool TryGetNextSetObject(SortedSetObject sortedSetObj, byte operation, out byte[] nextItem)
        {
            nextItem = default;

            // If object has no items, return
            if (sortedSetObj.Dictionary.Count == 0) return false;

            // Get the next object according to operation type
            switch ((SetOperation)operation)
            {
                default:
                    return false;
            }
        }

        /// <summary>
        /// Try to get next available item from object
        /// </summary>
        /// <param name="key">Key of object</param>
        /// <param name="storageSession">Current storage session</param>
        /// <param name="objectType">Type of object</param>
        /// <param name="operation">Operation type</param>
        /// <param name="currCount">Collection size</param>
        /// <param name="nextItem">Retrieved item</param>
        /// <returns>True if found available item</returns>
        private bool TryGetNextItem(byte[] key, StorageSession storageSession, GarnetObjectType objectType,
            byte operation, out int currCount, out byte[] nextItem)
        {
            currCount = default;
            nextItem = default;
            var createTransaction = false;

            // Create a transaction if not currently in a running transaction
            if (storageSession.txnManager.state != TxnState.Running)
            {
                Debug.Assert(storageSession.txnManager.state == TxnState.None);
                createTransaction = true;
                var asKey = storageSession.scratchBufferManager.CreateArgSlice(key);
                storageSession.txnManager.SaveKeyEntryToLock(asKey, true, LockType.Exclusive);
                _ = storageSession.txnManager.Run(true);
            }

            var objectLockableContext = storageSession.txnManager.ObjectStoreLockableContext;

            try
            {
                // Get the object stored at key
                var statusOp = storageSession.GET(key, out var osList, ref objectLockableContext);
                if (statusOp == GarnetStatus.NOTFOUND) return false;

                // Check for type match between the observer and the actual object type
                // If types match, get next item based on item type
                switch (osList.garnetObject)
                {
                    case ListObject listObj:
                        currCount = listObj.LnkList.Count;
                        if (objectType != GarnetObjectType.List) return false;
                        return TryGetNextListItem(listObj, operation, out nextItem);
                    case SortedSetObject setObj:
                        currCount = setObj.Dictionary.Count;
                        if (objectType != GarnetObjectType.SortedSet) return false;
                        return TryGetNextSetObject(setObj, operation, out nextItem);
                    default:
                        return false;
                }
            }
            finally
            {
                if (createTransaction)
                    storageSession.txnManager.Commit(true);
            }
        }

        /// <summary>
        /// A method that runs periodically and removes observers from queues
        /// whose status is SessionDisposed
        /// </summary>
        private void CleanUpDisposedObservers()
        {
            keysToObserversLock.EnterWriteLock();
            try
            {
                var keys = keysToObservers.Keys.ToArray();
                foreach (var key in keys)
                {
                    if (TryGetObserverQueue(key, out var observers))
                    {
                        var liveObservers = new Queue<CollectionItemObserver>();
                        while (observers.TryDequeue(out var observer))
                        {
                            observer.ObserverStatusLock.EnterReadLock();
                            try
                            {
                                if (observer.Status != ObserverStatus.SessionDisposed)
                                {
                                    liveObservers.Enqueue(observer);
                                }
                            }
                            finally
                            {
                                observer.ObserverStatusLock.ExitReadLock();
                            }
                        }

                        if (liveObservers.Count == 0)
                        {
                            keysToObservers.Remove(key);
                            continue;
                        }

                        keysToObservers[key] = liveObservers;
                    }
                }
            }
            finally
            {
                keysToObserversLock.ExitWriteLock();
            }
        }

        private void TryStop()
        {
            isStartedLock.EnterWriteLock();
            try
            {
                if (sessionIdToObserver.IsEmpty && brokerEventsQueue.Count == 0)
                {
                    stop = true;
                    isStarted = false;
                    while (brokerEventsQueue.Count > 0)
                        brokerEventsQueue.TryDequeue(out _);
                }
            }
            finally
            {
                isStartedLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Broker's main loop logic
        /// </summary>
        /// <returns>Task</returns>
        private async Task Start()
        {
            done.Reset();
            stop = false;

            var nextCleanUp = DateTime.Now + cleanUpPeriod;

            try
            {
                // Repeat while not disposed or cancelled
                while (!stop && !disposed && !cts.IsCancellationRequested)
                {
                    // Check if cleanup is due
                    if (DateTime.Now > nextCleanUp)
                    {
                        CleanUpDisposedObservers();
                        nextCleanUp = DateTime.Now + cleanUpPeriod;
                    }

                    // Set task to asynchronously dequeue next event in broker's queue
                    // once event is dequeued successfully, call handler method
                    try
                    {
                        await brokerEventsQueue.DequeueAsync(cts.Token).ContinueWith(t =>
                        {
                            if (t.Status == TaskStatus.RanToCompletion)
                                HandleBrokerEvent(t.Result);
                        }, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
            finally
            {
                done.Set();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            disposed = true;
            cts.Cancel();
            done.Wait();
        }

        /// <summary>
        /// This class defines an observer for a specific blocking command
        /// </summary>
        private class CollectionItemObserver
        {
            /// <summary>
            /// The session in which the blocking command was called
            /// </summary>
            internal RespServerSession Session { get; }

            /// <summary>
            /// The object type of the blocking command
            /// </summary>
            internal GarnetObjectType ObjectType { get; }

            /// <summary>
            /// The operation type for the blocking command
            /// </summary>
            internal byte Operation { get; }

            /// <summary>
            /// Status of the observer
            /// </summary>
            internal ObserverStatus Status { get; set; } = ObserverStatus.WaitingForResult;

            /// <summary>
            /// Result of the observer
            /// </summary>
            internal CollectionItemResult Result { get; private set; }

            /// <summary>
            /// Lock for the status of the observer
            /// </summary>
            internal ReaderWriterLockSlim ObserverStatusLock { get; } = new();

            /// <summary>
            /// Semaphore to notify the ResultSet status
            /// </summary>
            internal SemaphoreSlim ResultFoundSemaphore { get; } = new(0, 1);

            internal CollectionItemObserver(RespServerSession session, GarnetObjectType objectType, byte operation)
            {
                Session = session;
                ObjectType = objectType;
                Operation = operation;
            }

            /// <summary>
            /// Safely set the result for the observer
            /// </summary>
            /// <param name="result"></param>
            internal void HandleSetResult(CollectionItemResult result)
            {
                // If the result is already set or the observer session is disposed
                // There is no need to set the result
                if (Status != ObserverStatus.WaitingForResult)
                    return;

                ObserverStatusLock.EnterWriteLock();
                try
                {
                    if (Status != ObserverStatus.WaitingForResult)
                        return;

                    // Set the result, update the status and release the semaphore
                    Result = result;
                    Status = ObserverStatus.ResultSet;
                    ResultFoundSemaphore.Release();
                }
                finally
                {
                    ObserverStatusLock.ExitWriteLock();
                }
            }

            /// <summary>
            /// Safely set the status of the observer to reflect that its calling session was disposed
            /// </summary>
            internal void HandleSessionDisposed()
            {
                ObserverStatusLock.EnterWriteLock();
                try
                {
                    Status = ObserverStatus.SessionDisposed;
                }
                finally
                {
                    ObserverStatusLock.ExitWriteLock();
                }
            }
        }

        private enum ObserverStatus
        {
            // Observer is ready and waiting for result
            WaitingForResult,
            // Observer's result is set
            ResultSet,
            // Observer's calling RESP server session is disposed
            SessionDisposed,
        }

        /// <summary>
        /// Base class for events handled by CollectionItemBroker's main loop
        /// </summary>
        private abstract class BrokerEventBase
        {
        }

        /// <summary>
        /// Event to notify CollectionItemBroker that a collection has been updated
        /// </summary>
        private class CollectionUpdatedEvent : BrokerEventBase
        {
            /// <summary>
            /// Key of updated collection
            /// </summary>
            internal byte[] Key { get; }

            public CollectionUpdatedEvent(byte[] key)
            {
                Key = key;
            }
        }

        /// <summary>
        /// Event to notify CollectionItemBroker that a new observer was created
        /// </summary>
        private class NewObserverEvent : BrokerEventBase
        {
            /// <summary>
            /// The new observer instance
            /// </summary>
            internal CollectionItemObserver Observer { get; }

            /// <summary>
            /// The keys that the observer requests to subscribe on
            /// </summary>
            internal byte[][] Keys { get; }

            internal NewObserverEvent(CollectionItemObserver observer, byte[][] keys)
            {
                Observer = observer;
                Keys = keys;
            }
        }
    }

    /// <summary>
    /// Result of item retrieved from observed collection
    /// </summary>
    internal class CollectionItemResult
    {
        /// <summary>
        /// True if item was found
        /// </summary>
        internal bool Found => Key != default;

        /// <summary>
        /// Key of collection from which item was retrieved
        /// </summary>
        internal byte[] Key { get; }

        /// <summary>
        /// Item retrieved from collection
        /// </summary>
        internal byte[] Item { get; }

        /// <summary>
        /// Instance of empty result
        /// </summary>
        internal static readonly CollectionItemResult Empty = new(null, null);

        public CollectionItemResult(byte[] key, byte[] item)
        {
            Key = key;
            Item = item;
        }
    }
}