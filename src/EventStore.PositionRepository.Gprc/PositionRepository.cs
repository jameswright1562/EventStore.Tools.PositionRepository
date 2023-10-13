﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using EventStore.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Timer = System.Timers.Timer;

namespace EventStore.PositionRepository.Gprc
{

    public class PositionRepository : IPositionRepository
    {
        private readonly ILogger _log;
        private readonly string _positionStreamName;
        private readonly int _interval;
        public string PositionEventType { get; }
        private EventStoreClient _connection;
        private static Timer _timer;
        private Position _position = Position.Start;
        private Position _lastSavedPosition = Position.Start;

        public PositionRepository(string positionStreamName, string positionEventType, EventStoreClient client,
            ILogger logger, int interval = 1000)
        {
            _positionStreamName = positionStreamName;
            _connection = client;
            _interval = interval;
            PositionEventType = positionEventType;
            if (interval <= 0) return;
            _timer = new Timer(interval);
            _timer.Elapsed += _timer_Elapsed;
            _timer.Enabled = true;
            _log = logger;
            InitStream();
        }

        public PositionRepository(string positionStreamName, string positionEventType, EventStoreClient client,
            int interval = 1000) : this(positionStreamName, positionEventType, client,
            new SimpleConsoleLogger(nameof(PositionRepository)), interval)
        {
        }

        private void _timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_lastSavedPosition.Equals(_position))
                return;
            SavePosition();
        }

        private void SavePosition()
        {
            _connection.AppendToStreamAsync(_positionStreamName, StreamState.Any,
                new[] { new EventData(Uuid.FromGuid(Guid.NewGuid()), PositionEventType, 
                    SerializeObject(_position), null) }).Wait(); //Not sure what to do about the null metadata
            _lastSavedPosition = _position;
        }

        private void InitStream()
        {
            try
            {
                _connection?.SetStreamMetadataAsync(_positionStreamName, StreamState.Any,
                    SerializeMetadata(new Dictionary<string, int> { { "$maxCount", 1 } })).Wait();
                SavePosition();
            }
            catch (Exception ex)
            {
                _log.Error("Error while initializing stream", ex);
            }
        }

        public Position Get()
        {
            try
            {
                var evts = _connection.ReadStreamAsync(Direction.Backwards, _positionStreamName, StreamPosition.End, 20, true).ToArrayAsync().Result;
                _position = evts.Length != 0
                    ? DeserializeObject<Position>(evts.First().OriginalEvent.Data.ToArray())
                    : Position.Start;
            }
            catch (Exception e)
            {
                _log.Error($"Error while reading the position: {e.GetBaseException().Message}");
            }
            return _position;
        }

        public void Set(Position position)
        {
            _position = position;
            if (_interval <= 0)
                SavePosition();
        }

        private static StreamMetadata SerializeMetadata(object obj)
        {
            var jsonObj = JsonConvert.SerializeObject(obj);
            var data = Encoding.UTF8.GetBytes(jsonObj);
            return new StreamMetadata(customMetadata: System.Text.Json.JsonDocument.Parse(data));
        }

        private static ReadOnlyMemory<byte> SerializeObject(Position position)
        {
            var obj = JsonConvert.SerializeObject(position);
            return Encoding.UTF8.GetBytes(obj);
        }

        private static T DeserializeObject<T>(byte[] data)
        {
            var obj = Encoding.ASCII.GetString(data);
            return JsonConvert.DeserializeObject<T>(obj);
        }
    }
}