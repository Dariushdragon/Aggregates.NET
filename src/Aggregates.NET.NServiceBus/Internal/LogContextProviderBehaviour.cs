﻿using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    internal class LogContextProviderBehaviour : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogProvider.GetLogger("LogContextProvider");

        private readonly Configure _settings;

        public LogContextProviderBehaviour(Configure settings)
        {
            _settings = settings;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            // Populate the logging context with useful data from the messaeg
            using (LogProvider.OpenMappedContext("Instance", Defaults.Instance.ToString()))
            {
                string messageId = "";
                context.MessageHeaders.TryGetValue($"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}", out messageId);

                using (LogProvider.OpenMappedContext("MessageId", messageId))
                {
                    string corrId = "";
                    context.MessageHeaders.TryGetValue($"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}", out corrId);

                    if (string.IsNullOrEmpty(corrId))
                        corrId = messageId;
                    using (LogProvider.OpenMappedContext("CorrId", corrId))
                    {
                        using (LogProvider.OpenMappedContext("Endpoint", _settings.Endpoint))
                        {
                            Logger.DebugEvent("Start", "Processing [{MessageId:l}] Corr: [{CorrelationId:l}]", messageId, corrId);
                            await next().ConfigureAwait(false);
                            Logger.DebugEvent("End", "Processing [{MessageId:l}] Corr: [{CorrelationId:l}]", messageId, corrId);
                        }
                    }
                }
            }
        }
    }
    [ExcludeFromCodeCoverage]
    internal class LogContextProviderRegistration : RegisterStep
    {
        public LogContextProviderRegistration() : base(
            stepId: "LogContextProvider",
            behavior: typeof(LogContextProviderBehaviour),
            description: "Provides useful message information to logger")
        {
            InsertAfter("LocalMessageUnpack");
        }
    }
}
