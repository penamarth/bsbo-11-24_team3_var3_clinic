namespace PoliClick;

public sealed class Patient
{
    private readonly List<Appointment> appointments = [];

    public int Id { get; }
    public string FullName { get; }
    public DateOnly BirthDate { get; }
    public string PolicyNumber { get; }
    public bool IsAttached { get; }
    public IReadOnlyList<Appointment> Appointments => appointments.AsReadOnly();

    internal Patient(int id, string fullName, DateOnly birthDate,
        string policyNumber, bool isAttached)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        if (policyNumber is null || policyNumber.Length != 16 ||
            policyNumber.Any(c => c is < '0' or > '9'))
        {
            throw new ArgumentException("Номер полиса должен содержать 16 цифр.", nameof(policyNumber));
        }

        Id = id;
        FullName = fullName.Trim();
        BirthDate = birthDate;
        PolicyNumber = policyNumber;
        IsAttached = isAttached;
    }

    public bool HasActiveAppointment(string specialty) => appointments.Any(a =>
        a.IsActive && string.Equals(a.Doctor.Specialty, specialty, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<Appointment> GetMedicalHistory() => appointments
        .Where(a => a.Status == AppointmentStatus.COMPLETED)
        .OrderBy(a => a.StartedAt)
        .ToList().AsReadOnly();

    internal void AddAppointment(Appointment appointment) => appointments.Add(appointment);
}
