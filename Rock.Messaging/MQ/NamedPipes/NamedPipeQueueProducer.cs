﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Rock.Serialization;

namespace Rock.Messaging.NamedPipes
{
    public class NamedPipeQueueProducer : ISender
    {
        private readonly string _name;
        private readonly string _pipeName;
        private readonly ISerializer _serializer;
        private readonly BlockingCollection<string> _messages;
        private readonly Thread _runThread;

        internal NamedPipeQueueProducer(string name, string pipeName, ISerializer serializer)
        {
            _name = name;
            _pipeName = pipeName;
            _serializer = serializer;

            _messages = new BlockingCollection<string>();

            _runThread = new Thread(Run);
            _runThread.Start();
        }

        public string Name { get { return _name; } }

        public void Send(ISenderMessage message)
        {
            var messageString = _serializer.SerializeToString(message);
            _messages.Add(messageString);
        }

        public void Dispose()
        {
            _messages.CompleteAdding();
            _runThread.Join();
        }

        private void Run()
        {
            foreach (var message in _messages.GetConsumingEnumerable())
            {
                try
                {
                    var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);

                    try
                    {
                        pipe.Connect(0);
                    }
                    catch (TimeoutException)
                    {
                        continue;
                    }

                    using (var writer = new StreamWriter(pipe))
                    {
                        writer.WriteLine(message);
                    }
                }
                catch (Exception ex)
                {
                    // TODO: Something.
                    continue;
                }
            }
        }
    }
}