using System;
using System.Reflection;

namespace Styx.Helpers
{
    /// <summary>
    /// A required observation is incomplete. This is neither a negative result
    /// nor permission to continue the decision which requested that observation.
    /// Legacy readers still throw; independent lifecycle work can explicitly defer.
    /// </summary>
    public sealed class ObservationUnavailableException : InvalidOperationException
    {
        public string Observation { get; }

        public ObservationUnavailableException(string observation, string reason) : base(reason)
        {
            Observation = observation;
        }

        internal static ObservationUnavailableException? Find(Exception error)
        {
            // Reflection dispatch wraps the same unavailable observation. Do not
            // reinterpret arbitrary aggregates or type-initialization failures.
            for (int depth = 0; depth < 16; depth++)
            {
                if (error is ObservationUnavailableException unavailable) return unavailable;
                if (error is not TargetInvocationException { InnerException: not null } wrapped) return null;
                error = wrapped.InnerException;
            }
            return null;
        }
    }
}
