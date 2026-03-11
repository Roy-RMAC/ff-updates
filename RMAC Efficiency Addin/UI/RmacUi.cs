using Inventor;
using System;

namespace RMAC_Efficiency_Addin.UI
{
    internal sealed class RmacUi : IDisposable
    {
        private readonly RmacCommandRegistrar _registrar;
        private readonly RmacRibbonBuilder _builder;

        public RmacCommands Commands { get; }

        private bool _disposed;

        private RmacUi(RmacCommandRegistrar registrar, RmacRibbonBuilder builder, RmacCommands commands)
        {
            _registrar = registrar;
            _builder = builder;
            Commands = commands;
        }

        public static RmacUi Create(Inventor.Application app, string clientId, IRmacCommandCallbacks callbacks)
        {
            var registrar = new RmacCommandRegistrar(app, clientId, callbacks);
            var commands = registrar.Register();

            var builder = new RmacRibbonBuilder(app, clientId);
            builder.Build(commands);

            return new RmacUi(registrar, builder, commands);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _registrar.Unhook(Commands); } catch { }
        }

        public void RebuildRibbons()
        {
            try { _builder.Build(Commands); } catch { }
        }

    }
}
