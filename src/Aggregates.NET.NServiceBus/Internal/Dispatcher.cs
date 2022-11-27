﻿using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Routing;
using NServiceBus.Transport;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class Dispatcher : Contracts.IMessageDispatcher
    {
        private readonly ILogger Logger;
        private readonly IMessageSerializer _serializer;
        private readonly IEventMapper _mapper;
        private readonly IVersionRegistrar _registrar;
        private readonly NServiceBus.Transport.IMessageDispatcher _dispatcher;
        private readonly ReceiveAddresses _receiveAddresses;

        public Dispatcher(ILogger<Dispatcher> logger, NServiceBus.Transport.IMessageDispatcher dispatcher, IMessageSerializer serializer, IEventMapper mapper, IVersionRegistrar registrar, ReceiveAddresses receiveAddresses)
        {
            Logger = logger;
            _dispatcher = dispatcher;
            _serializer = serializer;
            _mapper = mapper;
            _registrar = registrar;
            _receiveAddresses = receiveAddresses;
        }



        public async Task SendLocal(IFullMessage message, IDictionary<string, string> headers = null)
        {
            headers = headers ?? new Dictionary<string, string>();

            var messageType = message.Message.GetType();
            if (!messageType.IsInterface)
                messageType = _mapper.GetMappedTypeFor(messageType) ?? messageType;

            var finalHeaders = message.Headers.Merge(headers);
            finalHeaders[Headers.EnclosedMessageTypes] = _registrar.GetVersionedName(messageType);
            finalHeaders[Headers.MessageIntent] = MessageIntent.Send.ToString();


            var messageId = Guid.NewGuid().ToString();
            var corrId = "";
            if (finalHeaders.ContainsKey($"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}"))
                messageId = finalHeaders[$"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}"];
            if (finalHeaders.ContainsKey($"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}"))
                corrId = finalHeaders[$"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}"];

            finalHeaders[Headers.MessageId] = messageId;
            finalHeaders[Headers.CorrelationId] = corrId;

            var bytes = _serializer.Serialize(message.Message);
            var messageBytes = new ReadOnlyMemory<byte>(bytes);

            var request = new OutgoingMessage(
                messageId: messageId,
                headers: finalHeaders,
                body: messageBytes);
            var operation = new TransportOperation(
                request,
                new UnicastAddressTag(_receiveAddresses.InstanceReceiveAddress));

            Logger.DebugEvent("SendLocal", "Starting local message [{MessageId:l}] Corr [{CorrelationId:l}]", messageId, corrId);
            await _dispatcher.Dispatch(
                outgoingMessages: new TransportOperations(operation),
                transaction: new TransportTransaction())
            .ConfigureAwait(false);

        }

    }
}
