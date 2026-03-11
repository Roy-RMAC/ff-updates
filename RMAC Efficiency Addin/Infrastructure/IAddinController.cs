using System;

namespace RMAC_Efficiency_Addin.Infrastructure
{
    internal interface IAddinController : IDisposable
    {
        void Start();
        void Stop();
        bool IsStarted { get; }
    }
}
