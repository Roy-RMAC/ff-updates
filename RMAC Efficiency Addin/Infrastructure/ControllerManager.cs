using System;
using System.Collections.Generic;

namespace RMAC_Efficiency_Addin.Infrastructure
{
    internal sealed class ControllerManager : IDisposable
    {
        private readonly List<IAddinController> _controllers = new();

        public void Add(IAddinController controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            _controllers.Add(controller);
        }

        public void StartAll()
        {
            foreach (var c in _controllers)
            {
                if (!c.IsStarted) c.Start();
            }
        }

        public void StopAll()
        {
            for (int i = _controllers.Count - 1; i >= 0; i--)
            {
                try { _controllers[i].Stop(); } catch { /* never throw on shutdown */ }
            }
        }

        public void Dispose()
        {
            StopAll();
            for (int i = _controllers.Count - 1; i >= 0; i--)
            {
                try { _controllers[i].Dispose(); } catch { }
            }
            _controllers.Clear();
        }
    }
}
