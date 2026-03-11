using Inventor;
using System;
using System.Collections.Generic;

namespace RMAC_Efficiency_Addin
{
    internal sealed class RecentPartsService
    {
        private readonly Inventor.Application _app;
        private readonly int _capacity;
        private readonly List<string> _items = new();

        private ApplicationEvents? _appEvents;
        private ApplicationEventsSink_OnSaveDocumentEventHandler? _onSaveDocHandler;

        public event Action? ListChanged;

        public RecentPartsService(Inventor.Application app, int capacity = 5)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _capacity = Math.Max(1, capacity);
        }

        public IReadOnlyList<string> Items => _items;

        public void Start()
        {
            if (_appEvents != null) return;

            _appEvents = _app.ApplicationEvents;

            // IMPORTANT: store delegate instance so unsubscribe works reliably
            _onSaveDocHandler = AppEvents_OnSaveDocument;
            _appEvents.OnSaveDocument += _onSaveDocHandler;
        }

        public void Stop()
        {
            if (_appEvents != null && _onSaveDocHandler != null)
            {
                try { _appEvents.OnSaveDocument -= _onSaveDocHandler; } catch { }
            }

            _onSaveDocHandler = null;
            _appEvents = null;

            _items.Clear();
            ListChanged?.Invoke();
        }

        private void AppEvents_OnSaveDocument(
            _Document DocumentObject,
            EventTimingEnum BeforeOrAfter,
            NameValueMap Context,
            out HandlingCodeEnum HandlingCode)
        {
            HandlingCode = HandlingCodeEnum.kEventNotHandled;
            if (BeforeOrAfter != EventTimingEnum.kAfter) return;

            try
            {
                if (DocumentObject is not PartDocument partDoc)
                    return;

                string fullName = "";
                try { fullName = partDoc.FullFileName ?? ""; } catch { fullName = ""; }

                if (string.IsNullOrWhiteSpace(fullName))
                    return;

                AddOrPromote(fullName);
            }
            catch
            {
                // Silent; never interfere with Inventor's save operation
            }
        }

        private void AddOrPromote(string fullPath)
        {
            // Remove existing entry (case-insensitive) so we don't duplicate.
            int existingIndex = _items.FindIndex(p =>
                string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
                _items.RemoveAt(existingIndex);

            // Insert at top
            _items.Insert(0, fullPath);

            // Trim from bottom to capacity
            while (_items.Count > _capacity)
                _items.RemoveAt(_items.Count - 1);

            ListChanged?.Invoke();
        }
    }
}
