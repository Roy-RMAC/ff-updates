// IPropertyTokenRegistry.cs
// Reusable catalogue of Inventor iProperty tokens.
// Each token records its display name, the Inventor property-set it lives in,
// and whether it takes a scope prefix (Part / Top) in naming templates.

using System;
using System.Collections.Generic;
using System.Linq;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// A single Inventor iProperty (or context value) that can appear in a naming template.
    /// </summary>
    internal sealed class IPropertyToken
    {
        /// <summary>Display name shown in the UI and used in template tokens, e.g. "Part Number".</summary>
        public string Name { get; }

        /// <summary>
        /// The Inventor property-set this property belongs to
        /// (empty for computed / context values that aren't real iProperties).
        /// </summary>
        public string PropertySet { get; }

        /// <summary>
        /// If true the token is scoped to a document and inserted as &lt;Part.Name&gt; or &lt;Top.Name&gt;.
        /// If false it is a context value inserted as &lt;Name&gt; with no scope prefix.
        /// </summary>
        public bool IsScoped { get; }

        public IPropertyToken(string name, string propertySet, bool isScoped = true)
        {
            Name = name;
            PropertySet = propertySet;
            IsScoped = isScoped;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// A named group of tokens (maps to an Inventor property-set or a logical group).
    /// </summary>
    internal sealed class IPropertyTokenGroup
    {
        public string Name { get; }
        public IReadOnlyList<IPropertyToken> Tokens { get; }

        public IPropertyTokenGroup(string name, IPropertyToken[] tokens)
        {
            Name = name;
            Tokens = Array.AsReadOnly(tokens);
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Static registry of every known Inventor iProperty token, grouped by property-set.
    /// Use <see cref="Groups"/> for grouped display or <see cref="All"/> for a flat list.
    /// </summary>
    internal static class IPropertyTokenRegistry
    {
        // ---- Inventor property-set name constants ----
        public const string SummaryInfo = "Inventor Summary Information";
        public const string DocSummaryInfo = "Inventor Document Summary Information";
        public const string DesignTracking = "Design Tracking Properties";
        public const string UserDefined = "Inventor User Defined Properties";

        /// <summary>All token groups in display order.</summary>
        public static readonly IReadOnlyList<IPropertyTokenGroup> Groups;

        /// <summary>Flat list of every token across all groups.</summary>
        public static readonly IReadOnlyList<IPropertyToken> All;

        static IPropertyTokenRegistry()
        {
            var groups = new List<IPropertyTokenGroup>
            {
                // ── Summary Information ──
                new("Summary Information", new[]
                {
                    new IPropertyToken("Title", SummaryInfo),
                    new IPropertyToken("Subject", SummaryInfo),
                    new IPropertyToken("Author", SummaryInfo),
                    new IPropertyToken("Keywords", SummaryInfo),
                    new IPropertyToken("Comments", SummaryInfo),
                    new IPropertyToken("Revision Number", SummaryInfo),
                }),

                // ── Document Summary ──
                new("Document Summary", new[]
                {
                    new IPropertyToken("Category", DocSummaryInfo),
                    new IPropertyToken("Manager", DocSummaryInfo),
                    new IPropertyToken("Company", DocSummaryInfo),
                }),

                // ── Design Tracking ──
                new("Design Tracking", new[]
                {
                    new IPropertyToken("Part Number", DesignTracking),
                    new IPropertyToken("Project", DesignTracking),
                    new IPropertyToken("Cost Center", DesignTracking),
                    new IPropertyToken("Checked By", DesignTracking),
                    new IPropertyToken("Date Checked", DesignTracking),
                    new IPropertyToken("Engr Approved By", DesignTracking),
                    new IPropertyToken("Engr Date Approved", DesignTracking),
                    new IPropertyToken("User Status", DesignTracking),
                    new IPropertyToken("Material", DesignTracking),
                    new IPropertyToken("Description", DesignTracking),
                    new IPropertyToken("Vendor", DesignTracking),
                    new IPropertyToken("Mfg Approved By", DesignTracking),
                    new IPropertyToken("Mfg Date Approved", DesignTracking),
                    new IPropertyToken("Cost", DesignTracking),
                    new IPropertyToken("Standard", DesignTracking),
                    new IPropertyToken("Design Status", DesignTracking),
                    new IPropertyToken("Designer", DesignTracking),
                    new IPropertyToken("Engineer", DesignTracking),
                    new IPropertyToken("Authority", DesignTracking),
                    new IPropertyToken("Manufacturer", DesignTracking),
                    new IPropertyToken("Stock Number", DesignTracking),
                    new IPropertyToken("Mass", DesignTracking),
                    new IPropertyToken("SurfaceArea", DesignTracking),
                    new IPropertyToken("Volume", DesignTracking),
                    new IPropertyToken("Density", DesignTracking),
                    new IPropertyToken("Flat Pattern Width", DesignTracking),
                    new IPropertyToken("Flat Pattern Length", DesignTracking),
                    new IPropertyToken("Flat Pattern Area", DesignTracking),
                }),
            };

            Groups = groups.AsReadOnly();
            All = groups.SelectMany(g => g.Tokens).ToList().AsReadOnly();
        }

        /// <summary>Look up a token by name (case-insensitive).</summary>
        public static IPropertyToken? FindByName(string name)
            => All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
