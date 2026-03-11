using System;
using System.Collections.Generic;

namespace RMAC_Efficiency_Addin.UI.Dialogs
{
    internal enum TokenKind
    {
        BuiltIn,   // handled by formatter explicitly (Qty, Thickness, etc.)
        IProperty, // maps to PropertySet + PropertyName
        CustomIProperty
    }

    internal sealed class TokenDef
    {
        public string DisplayName { get; }
        public TokenKind Kind { get; }

        // What gets inserted into the template text
        // Examples:
        //   "<Part Number>"
        //   "<Qty>"
        //   "<iProp:Design Tracking Properties|Description>"
        public string TokenText { get; }

        // For IProperty tokens only:
        public string? PropertySetName { get; }
        public string? PropertyName { get; }

        public TokenDef(string displayName, string tokenText, TokenKind kind,
            string? propertySetName = null, string? propertyName = null)
        {
            DisplayName = displayName ?? "";
            TokenText = tokenText ?? "";
            Kind = kind;
            PropertySetName = propertySetName;
            PropertyName = propertyName;
        }
    }

    internal static class TokenCatalog
    {
        // NOTE:
        // - TokenText is scope-less here; the dialog wraps scope when inserting (Part vs Asm).
        // - Built-ins should match what your BuildNameFromTemplate already replaces.
        // - iProp tokens are encoded as: <iProp:{Set}|{Name}>
        //   Then we can wrap scope later as: <Part:iProp:Set|Name> or <Asm:iProp:Set|Name>

        public static IReadOnlyList<TokenDef> All { get; } = Build();

        private static List<TokenDef> Build()
        {
            var t = new List<TokenDef>();

            // ---------- Built-ins (computed / known) ----------
            t.Add(new TokenDef("Part Number", "<Part Number>", TokenKind.BuiltIn));
            t.Add(new TokenDef("Description (built-in)", "<Description>", TokenKind.BuiltIn));
            t.Add(new TokenDef("Material (built-in)", "<Material>", TokenKind.BuiltIn));
            t.Add(new TokenDef("Stock Number (built-in)", "<Stock Number>", TokenKind.BuiltIn));
            t.Add(new TokenDef("Revision (built-in)", "<Revision>", TokenKind.BuiltIn));
            t.Add(new TokenDef("Thickness (built-in)", "<Thickness>", TokenKind.BuiltIn));
            t.Add(new TokenDef("Process", "<Process>", TokenKind.BuiltIn));
            t.Add(new TokenDef("Category", "<Category>", TokenKind.BuiltIn));
            t.Add(new TokenDef("QTY", "<Qty>", TokenKind.BuiltIn)); // inserted via QTY button too

            // ---------- iProperties: Design Tracking Properties ----------
            AddIProp(t, "DTP: Description", "Design Tracking Properties", "Description");
            AddIProp(t, "DTP: Part Number", "Design Tracking Properties", "Part Number");
            AddIProp(t, "DTP: Stock Number", "Design Tracking Properties", "Stock Number");
            AddIProp(t, "DTP: Material", "Design Tracking Properties", "Material");
            AddIProp(t, "DTP: Project", "Design Tracking Properties", "Project");
            AddIProp(t, "DTP: Vendor", "Design Tracking Properties", "Vendor");
            AddIProp(t, "DTP: Engineer", "Design Tracking Properties", "Engineer");
            AddIProp(t, "DTP: Cost Center", "Design Tracking Properties", "Cost Center");

            // ---------- iProperties: Inventor Summary Information ----------
            AddIProp(t, "Summary: Title", "Inventor Summary Information", "Title");
            AddIProp(t, "Summary: Subject", "Inventor Summary Information", "Subject");
            AddIProp(t, "Summary: Author", "Inventor Summary Information", "Author");
            AddIProp(t, "Summary: Keywords", "Inventor Summary Information", "Keywords");
            AddIProp(t, "Summary: Comments", "Inventor Summary Information", "Comments");

            // ---------- iProperties: Document Summary Information ----------
            AddIProp(t, "Doc Summary: Category", "Inventor Document Summary Information", "Category");
            AddIProp(t, "Doc Summary: Company", "Inventor Document Summary Information", "Company");
            AddIProp(t, "Doc Summary: Manager", "Inventor Document Summary Information", "Manager");

            // ---------- iProperties: User Defined Properties ----------
            // NOTE: Your thickness tool stores something in UDP; you can add your known ones here:
            AddIProp(t, "UDP: RMAC_Thickness", "Inventor User Defined Properties", "RMAC_Thickness");

            // Add more as needed from your log (once you paste it, I can populate this exactly)

            // ---------- Custom iProperty (placeholder item for UI) ----------
            t.Add(new TokenDef("(Custom iProperty…)", "<iProp:SET|NAME>", TokenKind.CustomIProperty));

            return t;
        }

        private static void AddIProp(List<TokenDef> list, string label, string setName, string propName)
        {
            list.Add(new TokenDef(
                label,
                tokenText: $"<iProp:{setName}|{propName}>",
                kind: TokenKind.IProperty,
                propertySetName: setName,
                propertyName: propName));
        }
    }
}
