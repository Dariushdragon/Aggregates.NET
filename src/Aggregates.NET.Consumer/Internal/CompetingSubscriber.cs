﻿using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace Aggregates.Internal
{
    /// <summary>
    /// Keeps track of the domain events it handles and imposes a limit on the amount of events to process (allowing other instances to process others)
    /// Used for load balancing
    /// </summary>
    public class CompetingSubscriber : IEventSubscriber, IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(CompetingSubscriber));
        private readonly IEventStoreConnection _client;
        private readonly IManageCompetes _competes;
        private readonly IDispatcher _dispatcher;
        private readonly ReadOnlySettings _settings;
        private readonly JsonSerializerSettings _jsonSettings;
        private readonly HashSet<Int32> _buckets;
        private readonly IDictionary<Int32, long> _seenBuckets;
        private readonly System.Threading.Timer _bucketHeartbeats;
        private readonly System.Threading.Timer _bucketPause;
        private readonly Int32 _bucketCount;
        private Boolean _pauseOnFreeBucket;
        private Boolean _pausedArmed;
        private Int32? _adopting;
        private Int64? _adoptingPosition;
        private Object _lock = new object();

        public Boolean ProcessingLive { get; set; }
        public Action<String, Exception> Dropped { get; set; }

        public CompetingSubscriber(IEventStoreConnection client, IManageCompetes competes, IDispatcher dispatcher, ReadOnlySettings settings, IMessageMapper mapper)
        {
            _client = client;
            _competes = competes;
            _dispatcher = dispatcher;
            _settings = settings;
            _buckets = new HashSet<Int32>();
            _seenBuckets = new Dictionary<Int32, long>();
            _bucketCount = _settings.Get<Int32>("BucketCount");
            _pauseOnFreeBucket = _settings.Get<Boolean>("PauseOnFreeBuckets");
            // Start paused initially if enabled
            dispatcher.Pause(_pauseOnFreeBucket);

            _jsonSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(mapper),
                ContractResolver = new EventContractResolver(mapper)
            };

            var period = TimeSpan.FromSeconds(_settings.Get<Int32>("BucketHeartbeats"));
            _bucketHeartbeats = new System.Threading.Timer((state) =>
            {
                Logger.Debug("Processing compete heartbeats");
                var handledBuckets = _settings.Get<Int32>("BucketsHandled");
                var expiration = _settings.Get<Int32>("BucketExpiration");

                var consumer = (CompetingSubscriber)state;
                var endpoint = consumer._settings.EndpointName();

                var notSeenBuckets = new HashSet<Int32>(consumer._buckets);
                IDictionary<Int32, long> seenBuckets;
                lock(_lock)
                {
                    seenBuckets = new Dictionary<Int32, long>(consumer._seenBuckets);
                    consumer._seenBuckets.Clear();
                }

                foreach (var seen in seenBuckets)
                {
                    if (consumer._buckets.Contains(seen.Key))
                    {
                        try
                        {
                            Logger.DebugFormat("Heartbeating bucket {0} position {1}", seen.Key, seen.Value);
                            consumer._competes.Heartbeat(endpoint, seen.Key, DateTime.UtcNow, seen.Value);
                        }
                        catch (DiscriminatorException)
                        {
                            // someone else took over the bucket
                            consumer._buckets.Remove(seen.Key);
                            Logger.InfoFormat("Lost claim on bucket {0}.  Total claimed: {1}/{2}", seen.Key, consumer._buckets.Count, handledBuckets);
                        }
                        notSeenBuckets.Remove(seen.Key);
                    }
                    else if (!consumer._adopting.HasValue && consumer._buckets.Count < handledBuckets)
                    {
                        var lastBeat = consumer._competes.LastHeartbeat(endpoint, seen.Key);
                        if (lastBeat.HasValue && (DateTime.UtcNow - lastBeat.Value).TotalSeconds > expiration)
                        {
                            Logger.DebugFormat("Last beat on bucket {0} is {1} - it is {2} seconds old, adopting...", seen.Key, lastBeat, (DateTime.UtcNow - lastBeat.Value).TotalSeconds);
                            // We saw new events but the consumer for this bucket has died, so we will adopt its bucket
                            AdoptBucket(consumer, endpoint, seen.Key);
                        }
                    }
                    else if (consumer._adopting == seen.Key)
                    {
                        try
                        {
                            Logger.DebugFormat("Heartbeating adopted bucket {0} position {1}", seen.Key, consumer._adoptingPosition.Value);
                            consumer._competes.Heartbeat(endpoint, seen.Key, DateTime.UtcNow, consumer._adoptingPosition.Value);
                        }
                        catch (DiscriminatorException)
                        {
                            // someone else took over the bucket
                            consumer._adopting = null;
                            consumer._adoptingPosition = null;
                            Logger.InfoFormat("Lost claim on adopted bucket {0}.  Total claimed: {1}/{2}", seen.Key, consumer._buckets.Count, handledBuckets);
                        }
                        notSeenBuckets.Remove(seen.Key);
                    }
                }

                var expiredBuckets = new List<Int32>();
                // Heartbeat the buckets we haven't seen but are still watching
                foreach (var bucket in notSeenBuckets)
                {
                    try
                    {
                        Logger.DebugFormat("Heartbeating unseen bucket {0}", bucket);
                        consumer._competes.Heartbeat(endpoint, bucket, DateTime.UtcNow);
                    }
                    catch (DiscriminatorException)
                    {
                        // someone else took over the bucket
                        consumer._buckets.Remove(bucket);
                        Logger.InfoFormat("Lost claim on bucket {0}.  Total claimed: {1}/{2}", bucket, consumer._buckets.Count, handledBuckets);
                    }
                }

            }, this, period, period);

            if (!_pauseOnFreeBucket) return;

            _bucketPause = new System.Threading.Timer(state =>
            {
                var consumer = (CompetingSubscriber)state;
                var endpoint = consumer._settings.EndpointName();
                var expiration = _settings.Get<Int32>("BucketExpiration");

                var openBuckets = consumer._bucketCount;

                // Check that all buckets are being watched

                Parallel.For(0, consumer._bucketCount, idx =>
                {
                    var lastBeat = consumer._competes.LastHeartbeat(endpoint, idx);
                    if (lastBeat.HasValue && (DateTime.UtcNow - lastBeat.Value).TotalSeconds < expiration)
                        Interlocked.Decrement(ref openBuckets);
                });

                if (openBuckets != 0 && _pausedArmed == false)
                {
                    Logger.WarnFormat("Detected {0} free buckets - pause ARMED", openBuckets);
                    _pausedArmed = true;
                }
                else if(openBuckets != 0 && _pausedArmed == true)
                {
                    Logger.WarnFormat("Detected {0} free buckets - PAUSING", openBuckets);
                    consumer._dispatcher.Pause(true);
                }
                else
                {
                    _pausedArmed = false;
                    consumer._dispatcher.Pause(false);
                }
                

            }, this, TimeSpan.FromSeconds(period.Seconds / 2), period);
            // Set open check to not run at the same moment as heartbeating
        }
        public void Dispose()
        {
            this._bucketHeartbeats.Dispose();
        }

        private static void AdoptBucket(CompetingSubscriber consumer, String endpoint, Int32 bucket)
        {
            var handledBuckets = consumer._settings.Get<Int32>("BucketsHandled");
            var readSize = consumer._settings.Get<Int32>("ReadSize");
            Logger.InfoFormat("Discovered orphaned bucket {0}.. adopting", bucket);
            if(!consumer._competes.Adopt(endpoint, bucket, DateTime.UtcNow))
            {
                Logger.InfoFormat("Failed to adopt bucket {0}.. maybe next time", bucket);
                return;
            }
            consumer._adopting = bucket;
            var lastPosition = consumer._competes.LastPosition(endpoint, bucket);
            consumer._adoptingPosition = lastPosition;

            var settings = new CatchUpSubscriptionSettings(readSize * 5, readSize, false, false);
            consumer._client.SubscribeToAllFrom(new Position(lastPosition, lastPosition), settings, (subscription, e) =>
            {
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;
                var eventBucket = Math.Abs(e.OriginalStreamId.GetHashCode() % consumer._bucketCount);
                if (eventBucket != bucket) return;

                Logger.DebugFormat("Adopted event appeared position {0}... processing - bucket {1}", e.OriginalPosition?.CommitPosition);
                if (!e.OriginalPosition.HasValue) return;

                var descriptor = e.Event.Metadata.Deserialize(consumer._jsonSettings);
                if (descriptor == null) return;

                var data = e.Event.Data.Deserialize(e.Event.EventType, consumer._jsonSettings);
                if (data == null) return;

                consumer._adoptingPosition = e.OriginalPosition?.CommitPosition ?? consumer._adoptingPosition;
                try
                {
                    consumer._dispatcher.Dispatch(data, descriptor, e.OriginalPosition?.CommitPosition);
                }
                catch (SubscriptionCanceled)
                {
                    subscription.Stop();
                    throw;
                }
            }, liveProcessingStarted: (sub) =>
            {
                sub.Stop();
                consumer._buckets.Add(bucket);
                consumer._adopting = null;
                consumer._adoptingPosition = null;
                Logger.InfoFormat("Successfully adopted bucket {0}.  Total claimed: {1}/{2}", bucket, consumer._buckets.Count, handledBuckets);
            }, subscriptionDropped: (subscription, reason, e) =>
            {
                if (reason == SubscriptionDropReason.UserInitiated) return;
                consumer._adopting = null;
                consumer._adoptingPosition = null;
                subscription.Stop();
                Logger.WarnFormat("While adopting bucket {0} the subscription dropped for reason: {1}.  Exception: {2}", bucket, reason, e);
            });
        }

        public void SubscribeToAll(String endpoint)
        {
            // To support HA simply save IManageCompetes data to a different db, in this way we can make clusters of consumers
            var handledBuckets = _settings.Get<Int32>("BucketsHandled");
            var readSize = _settings.Get<Int32>("ReadSize");

            // Start competing subscribers from the start, if they are picking up a new bucket they need to start from the begining
            Logger.InfoFormat("Endpoint '{0}' subscribing to all events from START", endpoint);
            var settings = new CatchUpSubscriptionSettings(readSize * 5, readSize, false, false);
            _client.SubscribeToAllFrom(Position.Start, settings, (subscription, e) =>
            {
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;


                var bucket = Math.Abs(e.OriginalStreamId.GetHashCode() % _bucketCount);

                if (!e.OriginalPosition.HasValue) return;

                if (!_buckets.Contains(bucket))
                {
                    // If we are already handling enough buckets, or we've seen (and tried to claim) it before, ignore
                    if (_buckets.Count >= handledBuckets || _seenBuckets.ContainsKey(bucket))
                    {
                        Logger.DebugFormat("Event appeared position {0}... skipping", e.OriginalPosition?.CommitPosition);
                        lock(_lock) _seenBuckets[bucket] = e.OriginalPosition.Value.CommitPosition;
                        return;
                    }
                    else
                    {
                        Logger.DebugFormat("Attempting to claim bucket {0}", bucket);
                        // Returns true if it claimed the bucket
                        if (_competes.CheckOrSave(endpoint, bucket, e.OriginalPosition.Value.CommitPosition))
                        {
                            _buckets.Add(bucket);
                            Logger.InfoFormat("Claimed bucket {0}.  Total claimed: {1}/{2}", bucket, _buckets.Count, handledBuckets);
                        }
                        else
                        {
                            lock(_lock) _seenBuckets[bucket] = e.OriginalPosition.Value.CommitPosition;
                            return;
                        }
                    }
                }
                Logger.DebugFormat("Event appeared position {0}... processing - bucket {1}", e.OriginalPosition?.CommitPosition, bucket);
                lock(_lock) _seenBuckets[bucket] = e.OriginalPosition.Value.CommitPosition;


                var descriptor = e.Event.Metadata.Deserialize(_jsonSettings);
                if (descriptor == null) return;

                var data = e.Event.Data.Deserialize(e.Event.EventType, _jsonSettings);

                // Data is null for certain irrelevant eventstore messages (and we don't need to store position or snapshots)
                if (data == null) return;
                
                try
                {
                    _dispatcher.Dispatch(data, descriptor, e.OriginalPosition?.CommitPosition);
                }
                catch (SubscriptionCanceled)
                {
                    subscription.Stop();
                    throw;
                }

            }, liveProcessingStarted: (_) =>
            {
                Logger.Info("Live processing started");
                ProcessingLive = true;
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.WarnFormat("Subscription dropped for reason: {0}.  Exception: {1}", reason, e);
                ProcessingLive = false;
                if (Dropped != null)
                    Dropped.Invoke(reason.ToString(), e);
            });
        }

    }
}
