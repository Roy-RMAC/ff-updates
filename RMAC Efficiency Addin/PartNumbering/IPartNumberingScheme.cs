namespace RMAC_Efficiency_Addin.PartNumbering
{
    internal interface IPartNumberingScheme
    {
        string Name { get; }
        void Run(RenumberingContext ctx, dynamic bomRows);
    }
}