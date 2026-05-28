namespace Prodimt.Pedidos.Domain.Entities;

public sealed class Machine
{
    public Guid Id { get; set; }

    public int Number { get; set; }

    public string? ExternalCode { get; set; }

    public string? Name { get; set; }

    public bool IsActive { get; set; } = true;

    public static Machine Create(int number, string? name)
    {
        var machine = new Machine
        {
            Id = Guid.NewGuid()
        };

        machine.Update(number, name);
        return machine;
    }

    public void SetExternalCode(string? externalCode)
    {
        ExternalCode = string.IsNullOrWhiteSpace(externalCode) ? null : externalCode.Trim();
    }

    public void Update(int number, string? name)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "El numero de maquina debe ser mayor a cero.");
        }

        Number = number;
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
