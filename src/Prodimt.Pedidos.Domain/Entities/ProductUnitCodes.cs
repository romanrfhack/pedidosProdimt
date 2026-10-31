namespace Prodimt.Pedidos.Domain.Entities;

/// <summary>Sale units. A null unit is unknown, never an implicit kilogram.</summary>
public static class ProductUnitCodes
{
    public const string Kilogram = "kg";
    public const string Piece = "piece";
    public const string Dozen = "dozen";
    public const string Package = "package";

    public static bool IsSupported(string unitCode) =>
        unitCode is Kilogram or Piece or Dozen or Package;
}
