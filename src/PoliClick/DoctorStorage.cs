namespace PoliClick;

public sealed class DoctorStorage
{
    private readonly List<Doctor> doctors = [];

    public IReadOnlyList<Doctor> Doctors => doctors.AsReadOnly();

    internal Doctor AddDoctor(string fullName, string specialty, int slotMinutes)
    {
        var doctor = new Doctor(doctors.Count + 1, fullName, specialty, slotMinutes);
        if (doctors.Any(d => string.Equals(d.Specialty, doctor.Specialty, StringComparison.OrdinalIgnoreCase) &&
            d.SlotMinutes != doctor.SlotMinutes))
        {
            throw new InvalidOperationException("Длительность слота должна быть одинаковой для врачей одной специальности.");
        }

        doctors.Add(doctor);
        return doctor;
    }

    public Doctor GetDoctor(int doctorId) => doctors.FirstOrDefault(d => d.Id == doctorId)
        ?? throw new KeyNotFoundException("Врач не найден.");

    public IReadOnlyList<string> GetSpecialties() => doctors
        .Select(d => d.Specialty)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
        .ToList().AsReadOnly();

    public IReadOnlyList<Doctor> GetDoctors(string specialty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specialty);
        return doctors.Where(d => string.Equals(d.Specialty, specialty.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.FullName).ThenBy(d => d.Id)
            .ToList().AsReadOnly();
    }
}
