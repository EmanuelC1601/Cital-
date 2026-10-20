namespace Belleza.Domain;

/// <summary>Servicio ofrecido por un negocio; el precio se expresa en MXN.</summary>
public sealed class Service
{
    private Service() { }

    public Service(Guid businessId, string name, string description, decimal price, int durationMinutes, int bufferMinutes)
    {
        Validate(name, price, durationMinutes, bufferMinutes);
        Id = Guid.NewGuid();
        BusinessId = businessId;
        Name = name.Trim();
        Description = description.Trim();
        Price = price;
        DurationMinutes = durationMinutes;
        BufferMinutes = bufferMinutes;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public string Name { get; private set; } = "";
    public string Description { get; private set; } = "";
    public decimal Price { get; private set; }
    public int DurationMinutes { get; private set; }
    public int BufferMinutes { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string description, decimal price, int durationMinutes, int bufferMinutes, bool isActive)
    {
        Validate(name, price, durationMinutes, bufferMinutes);
        Name = name.Trim();
        Description = description.Trim();
        Price = price;
        DurationMinutes = durationMinutes;
        BufferMinutes = bufferMinutes;
        IsActive = isActive;
    }

    private static void Validate(string name, decimal price, int duration, int buffer)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) throw new ArgumentException("El nombre es obligatorio y admite hasta 100 caracteres.");
        if (price < 0 || price > 999999.99m || decimal.Round(price, 2) != price) throw new ArgumentOutOfRangeException(nameof(price), "El precio debe estar entre $0 y $999,999.99 MXN.");
        if (duration is < 5 or > 480) throw new ArgumentOutOfRangeException(nameof(duration), "La duración debe ser de 5 a 480 minutos.");
        if (buffer is < 0 or > 180) throw new ArgumentOutOfRangeException(nameof(buffer), "El tiempo adicional debe ser de 0 a 180 minutos.");
    }
}
