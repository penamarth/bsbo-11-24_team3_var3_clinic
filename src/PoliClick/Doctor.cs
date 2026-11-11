namespace PoliClick;

public sealed class Doctor
{
    public int Id { get; }
    public string FullName { get; }
    public string Specialty { get; }
    public int SlotMinutes { get; }

    internal Doctor(int id, string fullName, string specialty, int slotMinutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(specialty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slotMinutes);

        Id = id;
        FullName = fullName.Trim();
        Specialty = specialty.Trim();
        SlotMinutes = slotMinutes;
    }
}
